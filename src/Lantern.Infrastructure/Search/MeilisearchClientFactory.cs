using Meilisearch;

namespace Lantern.Infrastructure.Search;

/// <summary>
/// Returns cached <see cref="MeilisearchClient"/> instances bound to the
/// credential of the requested role. Clients are created lazily on first
/// access and reused for the lifetime of the factory; rotation requires
/// replacing the factory (handled by the credential manager at restart).
/// </summary>
public sealed class MeilisearchClientFactory : IMeilisearchClientFactory
{
    private readonly MeilisearchOptions _options;
    private readonly IMeilisearchCredentialManager _credentialManager;
    private readonly object _gate = new();
    private readonly Dictionary<MeilisearchCredentialRole, MeilisearchClient> _clients = new();

    public MeilisearchClientFactory(
        MeilisearchOptions options,
        IMeilisearchCredentialManager credentialManager)
    {
        _options = options;
        _credentialManager = credentialManager;
    }

    public MeilisearchClient GetSearchClient() => Get(MeilisearchCredentialRole.Search);

    public MeilisearchClient GetIndexingClient() => Get(MeilisearchCredentialRole.Indexing);

    public MeilisearchClient GetAdminClient() => Get(MeilisearchCredentialRole.Admin);

    private MeilisearchClient Get(MeilisearchCredentialRole role)
    {
        lock (_gate)
        {
            if (_clients.TryGetValue(role, out MeilisearchClient? existing))
                return existing;

            MeilisearchCredential credential = _credentialManager.GetCredential(role);
            var client = new MeilisearchClient(_options.Url, credential.KeyValue);
            _clients[role] = client;
            return client;
        }
    }
}
