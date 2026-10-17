namespace Lantern.Core.Search;

/// <summary>
/// Defines the contract for a full-text search service.
/// Not yet implemented — planned for Phase 1.
/// </summary>
public interface ISearchService
{
    Task<SearchResult> SearchAsync(string indexUid, string query, CancellationToken cancellationToken = default);
    Task IndexDocumentAsync(string indexUid, SearchDocument document, CancellationToken cancellationToken = default);
    Task DeleteIndexAsync(string indexUid, CancellationToken cancellationToken = default);
}
