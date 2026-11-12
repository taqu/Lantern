using Meilisearch;

namespace Lantern.Infrastructure.Search;

/// <summary>
/// Returns <see cref="MeilisearchClient"/> instances whose Bearer token matches
/// the requested logical role. Each role's client is cached for the process
/// lifetime; callers receive a shared, thread-safe instance.
/// </summary>
public interface IMeilisearchClientFactory
{
    /// <summary>Search-only client (<c>search</c> action; <c>lantern-*</c> indexes).</summary>
    MeilisearchClient GetSearchClient();

    /// <summary>
    /// Indexing client for background crawlers and indexing services
    /// (documents, index + settings management, task inspection).
    /// </summary>
    MeilisearchClient GetIndexingClient();

    /// <summary>
    /// Administrative client for authorized server-side maintenance.
    /// Includes <c>Indexing</c> + task cancellation, dumps, and snapshots;
    /// excludes <c>keys.*</c>, which stays with the master key.
    /// </summary>
    MeilisearchClient GetAdminClient();
}
