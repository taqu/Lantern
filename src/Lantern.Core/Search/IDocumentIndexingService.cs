namespace Lantern.Core.Search;

public interface IDocumentIndexingService
{
    Task EnsureIndexAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(SearchDocument document, CancellationToken cancellationToken = default);
    // Submits a document upsert and returns the Meilisearch task UID without waiting for completion.
    Task<int> SubmitUpsertAsync(SearchDocument document, CancellationToken cancellationToken = default);
    Task UpsertBatchAsync(IEnumerable<SearchDocument> documents, CancellationToken cancellationToken = default);
    Task DeleteAsync(string documentId, CancellationToken cancellationToken = default);
    Task DeleteBatchAsync(IEnumerable<string> documentIds, CancellationToken cancellationToken = default);
    Task RebuildAsync(IEnumerable<SearchDocument> documents, CancellationToken cancellationToken = default);
}
