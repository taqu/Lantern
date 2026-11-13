using Lantern.Infrastructure.Search;
using Lantern.Infrastructure.Secrets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lantern.IntegrationTests;

/// <summary>
/// Unit tests for <see cref="MeilisearchCredentialManager"/> covering the
/// no-master-key fallback path: when a master key is unavailable, the manager
/// recovers the three role credentials from the local <see cref="ISecretStore"/>
/// cache (the layout operators seed when pointing Lantern at an externally
/// managed Meilisearch instance).
/// </summary>
public sealed class MeilisearchCredentialManagerTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "credmgr-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if(Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public async Task EnsureCredentials_NoMasterKey_LoadsAllRolesFromSecretStore()
    {
        FileSecretStore store = NewStore();
        await store.WriteAsync("meili-search-key", "search-value", CancellationToken.None);
        await store.WriteAsync("meili-indexing-key", "indexing-value", CancellationToken.None);
        await store.WriteAsync("meili-admin-key", "admin-value", CancellationToken.None);

        MeilisearchCredentialManager mgr = NewManager(store, masterKey: null);

        await mgr.EnsureCredentialsAsync(CancellationToken.None);

        Assert.True(mgr.IsReady);
        Assert.Equal("search-value", mgr.GetCredential(MeilisearchCredentialRole.Search).KeyValue);
        Assert.Equal("indexing-value", mgr.GetCredential(MeilisearchCredentialRole.Indexing).KeyValue);
        Assert.Equal("admin-value", mgr.GetCredential(MeilisearchCredentialRole.Admin).KeyValue);
    }

    [Fact]
    public async Task EnsureCredentials_NoMasterKey_MissingCacheEntry_Throws()
    {
        FileSecretStore store = NewStore();
        // Only two of the three required role keys are present.
        await store.WriteAsync("meili-search-key", "s", CancellationToken.None);
        await store.WriteAsync("meili-indexing-key", "i", CancellationToken.None);

        MeilisearchCredentialManager mgr = NewManager(store, masterKey: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => mgr.EnsureCredentialsAsync(CancellationToken.None));
        Assert.False(mgr.IsReady);
    }

    [Fact]
    public void GetCredential_BeforeEnsure_Throws()
    {
        MeilisearchCredentialManager mgr = NewManager(NewStore(), masterKey: null);
        Assert.Throws<InvalidOperationException>(
            () => mgr.GetCredential(MeilisearchCredentialRole.Search));
    }

    [Fact]
    public async Task EnsureCredentials_IsIdempotent()
    {
        FileSecretStore store = NewStore();
        await store.WriteAsync("meili-search-key", "s", CancellationToken.None);
        await store.WriteAsync("meili-indexing-key", "i", CancellationToken.None);
        await store.WriteAsync("meili-admin-key", "a", CancellationToken.None);

        MeilisearchCredentialManager mgr = NewManager(store, masterKey: null);
        await mgr.EnsureCredentialsAsync(CancellationToken.None);
        await mgr.EnsureCredentialsAsync(CancellationToken.None);

        Assert.True(mgr.IsReady);
        Assert.Equal("s", mgr.GetCredential(MeilisearchCredentialRole.Search).KeyValue);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private FileSecretStore NewStore() =>
        new(Options.Create(new SecretStoreOptions { Directory = _dir }));

    private static MeilisearchCredentialManager NewManager(ISecretStore store, string? masterKey) =>
        new(
            new MeilisearchOptions { Url = "http://127.0.0.1:7700" },
            new StubMasterKeyAccessor(masterKey),
            store,
            NullLogger<MeilisearchCredentialManager>.Instance);

    private sealed class StubMasterKeyAccessor(string? value) : IMasterKeyAccessor
    {
        public Task<string?> TryReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(value);
    }
}
