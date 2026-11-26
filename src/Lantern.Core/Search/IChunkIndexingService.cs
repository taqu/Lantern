namespace Lantern.Core.Search;

public interface IChunkIndexingService
{
    Task EnsureIndexAsync(CancellationToken cancellationToken = default);
    Task UpsertChunksAsync(IEnumerable<SearchChunk> chunks, CancellationToken cancellationToken = default);
    Task DeleteChunksByDocumentAsync(string documentId, CancellationToken cancellationToken = default);
    // Returns the Meilisearch task UID of the submitted chunk upsert, or null when hybrid
    // search is disabled or the document has no chunks. The UID should be tracked for
    // reconciliation because the chunk task calls the REST embedder and can fail independently.
    Task<int?> ReplaceDocumentChunksAsync(string documentId, IEnumerable<SearchChunk> newChunks, CancellationToken cancellationToken = default);
    Task DeleteChunkAsync(string chunkId, CancellationToken cancellationToken = default);
}
