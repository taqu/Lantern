namespace Lantern.Core.Administration;

public sealed record SearchEngineStatus(bool Available, string IndexName, long? DocumentCount, string Message);

public interface ISearchEngineHealthService
{
    Task<SearchEngineStatus> GetAsync(bool refresh = false, CancellationToken cancellationToken = default);
}

/// <summary>
/// Runtime status of the embedding module and hybrid search pipeline.
/// </summary>
public sealed record EmbeddingHealthStatus(
    bool EmbeddingEnabled,
    string ModelStatus,
    bool HybridEnabled,
    string ChunkIndexName,
    long? ChunkDocumentCount);
