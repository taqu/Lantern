namespace Lantern.Core.Extraction;

public sealed record ExtractedBlock(
    BlockType Type,
    string Text,
    string? SectionTitle = null,
    int? PageNumber = null,
    int? SlideNumber = null,
    string? SheetName = null,
    int? StartOffset = null,
    int? EndOffset = null,
    int HeadingLevel = 0);
