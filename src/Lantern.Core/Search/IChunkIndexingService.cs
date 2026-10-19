namespace Lantern.Core.Search;

public interface IChunkIndexingService
{
    Task EnsureIndexAsync(CancellationToken cancellationToken = default);
    Task UpsertChunksAsync(IEnumerable<SearchChunk> chunks, CancellationToken cancellationToken = default);
    Task DeleteChunksByDocumentAsync(string documentId, CancellationToken cancellationToken = default);
    Task ReplaceDocumentChunksAsync(string documentId, IEnumerable<SearchChunk> newChunks, CancellationToken cancellationToken = default);
    Task DeleteChunkAsync(string chunkId, CancellationToken cancellationToken = default);
}
