using Lantern.Core.Search;

namespace Lantern.Core.Extraction;

public sealed class DocumentProcessingResult
{
    public bool Success { get; init; }
    public ExtractedDocument? Document { get; init; }
    public IReadOnlyList<SearchChunk>? Chunks { get; init; }
    public ExtractionFailureCategory? FailureCategory { get; init; }
    public string? FailureMessage { get; init; }
}
