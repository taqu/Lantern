namespace Lantern.Core.Search;

/// <summary>
/// Defines the contract for a full-text search service.
/// </summary>
public interface ISearchService
{
    // Phase 1: paginated search for the UI
    Task<SearchPageResponse> SearchPageAsync(
        string query,
        int page,
        CancellationToken cancellationToken = default);

    // Phase 0 methods kept for compatibility
    Task<SearchResult> SearchAsync(string indexUid, string query, CancellationToken cancellationToken = default);
    Task IndexDocumentAsync(string indexUid, SearchDocument document, CancellationToken cancellationToken = default);
    Task DeleteIndexAsync(string indexUid, CancellationToken cancellationToken = default);
}
