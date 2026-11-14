using Lantern.Infrastructure.Search;
using Meilisearch;

namespace Lantern.IntegrationTests;

/// <summary>
/// Test-only <see cref="IMeilisearchClientFactory"/> that returns a single
/// pre-constructed <see cref="MeilisearchClient"/> for every role. Used by the
/// Meilisearch integration tests which pre-date the role-aware factory and
/// still construct the raw client directly.
/// </summary>
internal sealed class TestMeilisearchClientFactory : IMeilisearchClientFactory
{
    private readonly MeilisearchClient _client;

    public TestMeilisearchClientFactory(MeilisearchClient client) => _client = client;

    public Task EnsureClientsAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public MeilisearchClient GetSearchClient() => _client;

    public MeilisearchClient GetIndexingClient() => _client;

    public MeilisearchClient GetAdminClient() => _client;
}
