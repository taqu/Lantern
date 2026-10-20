namespace Lantern.Core.Extraction;

public sealed class ExtractedDocument
{
    public required string Title { get; init; }
    public required IReadOnlyList<ExtractedBlock> Blocks { get; init; }
    public required ExtractedDocumentMetadata Metadata { get; init; }
}
