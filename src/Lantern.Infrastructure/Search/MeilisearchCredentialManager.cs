using Lantern.Infrastructure.Secrets;
using Meilisearch;
using Meilisearch.QueryParameters;
using Microsoft.Extensions.Logging;

namespace Lantern.Infrastructure.Search;

/// <summary>
/// Owns the role-scoped Meilisearch API key lifecycle. Reuses keys that already
/// exist on the server (looked up by deterministic Name) and provisions any
/// missing ones through <c>POST /keys</c> using the configured master key.
/// Persists resolved key values via <see cref="ISecretStore"/> as a local
/// cache; the server remains the source of truth.
/// </summary>
public sealed class MeilisearchCredentialManager : IMeilisearchCredentialManager
{
    private readonly MeilisearchOptions _meiliOptions;
    private readonly IMasterKeyAccessor _masterKeyAccessor;
    private readonly ISecretStore _secretStore;
    private readonly ILogger<MeilisearchCredentialManager> _logger;

    private readonly SemaphoreSlim _ensureLock = new(1, 1);
    private readonly Dictionary<MeilisearchCredentialRole, MeilisearchCredential> _credentials = new();
    private volatile bool _ready;

    public MeilisearchCredentialManager(
        MeilisearchOptions meiliOptions,
        IMasterKeyAccessor masterKeyAccessor,
        ISecretStore secretStore,
        ILogger<MeilisearchCredentialManager> logger)
    {
        _meiliOptions = meiliOptions;
        _masterKeyAccessor = masterKeyAccessor;
        _secretStore = secretStore;
        _logger = logger;
    }

    public bool IsReady => _ready;

    public async Task EnsureCredentialsAsync(CancellationToken cancellationToken)
    {
        await _ensureLock.WaitAsync(cancellationToken);
        try
        {
            if (_ready) return;

            string? masterKey = await _masterKeyAccessor.TryReadAsync(cancellationToken);
            if (string.IsNullOrEmpty(masterKey))
            {
                // No master key — try to recover role keys from the local secret store.
                // This supports external Meilisearch instances where the operator
                // provisioned the role keys out-of-band.
                if (await TryLoadFromSecretStoreAsync(cancellationToken))
                {
                    _ready = true;
                    _logger.LogInformation(
                        "Meilisearch credentials loaded from local secret store (no master key available).");
                    return;
                }
                throw new InvalidOperationException(
                    "Meilisearch master key is not available and no cached role keys were found. " +
                    "Run 'Lantern.Web setup' or configure MeilisearchProcess:MasterKeyFile.");
            }

            var adminClient = new MeilisearchClient(_meiliOptions.Url, masterKey);
            IReadOnlyList<Key> existing = await ListAllKeysAsync(adminClient, cancellationToken);

            foreach (RolePolicy policy in RolePolicy.All)
            {
                MeilisearchCredential credential =
                    await ReconcileRoleAsync(adminClient, existing, policy, cancellationToken);
                _credentials[policy.Role] = credential;
                await _secretStore.WriteAsync(policy.SecretName, credential.KeyValue, cancellationToken);
            }

            _ready = true;
            _logger.LogInformation("Meilisearch credentials provisioned for all roles.");

            await AdviseLegacyCleanupAsync(cancellationToken);
        }
        finally
        {
            _ensureLock.Release();
        }
    }

    public MeilisearchCredential GetCredential(MeilisearchCredentialRole role)
    {
        if (!_ready)
            throw new InvalidOperationException(
                "EnsureCredentialsAsync must complete before credentials can be resolved.");
        if (!_credentials.TryGetValue(role, out MeilisearchCredential? credential))
            throw new InvalidOperationException($"No credential provisioned for role {role}.");
        return credential;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task AdviseLegacyCleanupAsync(CancellationToken ct)
    {
        // The legacy single-key design stored its credential under 'meili-api-key'.
        // Its contents may be either the Meilisearch master key (dev-seeded) or a
        // KeyAction.All /keys entry (older prod installs). We do NOT auto-delete
        // either — the mission requires explicit operator intervention — but we
        // emit a one-time advisory so the file stops lingering unnoticed.
        string? legacy = await _secretStore.TryReadAsync("meili-api-key", ct);
        if (!string.IsNullOrEmpty(legacy))
        {
            _logger.LogWarning(
                "Legacy Meilisearch credential 'meili-api-key' found in the secret store. " +
                "It is no longer used by Lantern. Remove the file manually once you have " +
                "confirmed no external client depends on it.");
        }
    }

    private async Task<bool> TryLoadFromSecretStoreAsync(CancellationToken ct)
    {
        foreach (RolePolicy policy in RolePolicy.All)
        {
            string? value = await _secretStore.TryReadAsync(policy.SecretName, ct);
            if (string.IsNullOrEmpty(value)) return false;
            _credentials[policy.Role] = new MeilisearchCredential(policy.Role, value, null, null);
        }
        return true;
    }

    private static async Task<IReadOnlyList<Key>> ListAllKeysAsync(
        MeilisearchClient client, CancellationToken ct)
    {
        var results = new List<Key>();
        int offset = 0;
        const int pageSize = 100;
        while (true)
        {
            var page = await client.GetKeysAsync(
                new KeysQuery { Limit = pageSize, Offset = offset }, ct);
            if (page.Results is null) break;
            results.AddRange(page.Results);
            int fetched = results.Count;
            if (fetched >= page.Total || page.Results.Count() < pageSize) break;
            offset += pageSize;
        }
        return results;
    }

    private async Task<MeilisearchCredential> ReconcileRoleAsync(
        MeilisearchClient adminClient,
        IReadOnlyList<Key> existingKeys,
        RolePolicy policy,
        CancellationToken ct)
    {
        Key? existing = existingKeys.FirstOrDefault(k => k.Name == policy.Name);
        if (existing is not null)
        {
            if (KeyMatchesPolicy(existing, policy))
            {
                _logger.LogDebug("Reusing existing Meilisearch key for {Role}.", policy.Role);
                return new MeilisearchCredential(
                    policy.Role,
                    existing.KeyUid ?? throw new InvalidOperationException(
                        $"Meilisearch did not return a key value for '{policy.Name}'."),
                    existing.Uid,
                    existing.ExpiresAt);
            }
            _logger.LogWarning(
                "Meilisearch key '{Name}' exists but permissions drifted from policy; recreating.",
                policy.Name);
            if (!string.IsNullOrEmpty(existing.Uid))
                await adminClient.DeleteKeyAsync(existing.Uid, ct);
        }

        var keyBody = new Key
        {
            Name = policy.Name,
            Description = policy.Description,
            Actions = policy.Actions,
            Indexes = policy.Indexes,
            ExpiresAt = null,
        };
        Key created = await adminClient.CreateKeyAsync(keyBody, ct);
        string keyValue = created.KeyUid
            ?? throw new InvalidOperationException(
                $"Meilisearch did not return a key value for created key '{policy.Name}'.");
        _logger.LogInformation("Provisioned new Meilisearch key for {Role}.", policy.Role);
        return new MeilisearchCredential(policy.Role, keyValue, created.Uid, created.ExpiresAt);
    }

    private static bool KeyMatchesPolicy(Key existing, RolePolicy policy)
    {
        HashSet<KeyAction> expectedActions = policy.Actions.ToHashSet();
        HashSet<KeyAction> actualActions = (existing.Actions ?? []).ToHashSet();
        if (!expectedActions.SetEquals(actualActions)) return false;

        HashSet<string> expectedIndexes = policy.Indexes.ToHashSet(StringComparer.Ordinal);
        HashSet<string> actualIndexes = (existing.Indexes ?? []).ToHashSet(StringComparer.Ordinal);
        return expectedIndexes.SetEquals(actualIndexes);
    }

    private sealed record RolePolicy(
        MeilisearchCredentialRole Role,
        string Name,
        string Description,
        string SecretName,
        IReadOnlyList<KeyAction> Actions,
        IReadOnlyList<string> Indexes)
    {
        public static readonly IReadOnlyList<RolePolicy> All =
        [
            new(MeilisearchCredentialRole.Search,
                Name: "Lantern Search",
                Description: "Scoped search-only key used by Lantern query handlers.",
                SecretName: "meili-search-key",
                Actions: [KeyAction.Search],
                Indexes: ["lantern-*"]),

            new(MeilisearchCredentialRole.Indexing,
                Name: "Lantern Indexing",
                Description: "Indexing and task-inspection key used by Lantern crawlers.",
                SecretName: "meili-indexing-key",
                Actions:
                [
                    KeyAction.Search,
                    KeyAction.DocumentsAll,
                    KeyAction.IndexesAll,
                    KeyAction.TasksGet,
                    KeyAction.SettingsAll,
                    KeyAction.StatsGet,
                ],
                Indexes: ["lantern-*"]),

            new(MeilisearchCredentialRole.Admin,
                Name: "Lantern Admin",
                Description: "Administrative key for authorized server-side maintenance (excludes keys.*).",
                SecretName: "meili-admin-key",
                Actions:
                [
                    KeyAction.Search,
                    KeyAction.DocumentsAll,
                    KeyAction.IndexesAll,
                    KeyAction.TasksGet,
                    KeyAction.TasksCancel,
                    KeyAction.TasksDelete,
                    KeyAction.SettingsAll,
                    KeyAction.StatsGet,
                    KeyAction.DumpsCreate,
                    KeyAction.Version,
                ],
                Indexes: ["lantern-*"]),
        ];
    }
}
