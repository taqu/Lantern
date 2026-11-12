namespace Lantern.Infrastructure.Search;

/// <summary>
/// Logical Meilisearch credential roles used by Lantern.
/// Each role maps to a scoped Meilisearch API key provisioned through the
/// <c>/keys</c> endpoint with the minimum necessary actions and index scope.
/// </summary>
/// <remarks>
/// The master key is deliberately NOT a role: it is used only to create and
/// manage the scoped keys below and is never consumed by ordinary services.
/// </remarks>
public enum MeilisearchCredentialRole
{
    /// <summary>Read-only search access. Actions: <c>search</c>.</summary>
    Search = 1,

    /// <summary>
    /// Document write + index/settings management for background crawlers and
    /// indexing services. Actions: <c>search</c>, <c>documents.*</c>,
    /// <c>indexes.*</c>, <c>tasks.get</c>, <c>settings.*</c>, <c>stats.get</c>.
    /// </summary>
    Indexing = 2,

    /// <summary>
    /// Authorized server-side administrative operations: indexing-plus
    /// task management, dumps/snapshots, and version diagnostics. Excludes
    /// <c>keys.*</c>, which remains with the master key alone.
    /// </summary>
    Admin = 3,
}
