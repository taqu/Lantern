namespace Lantern.Core.Extraction;

public sealed record ExtractedDocumentMetadata(
    string? Title = null,
    string? Author = null,
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? ModifiedAt = null,
    DocumentFormat Format = DocumentFormat.Unknown,
    int? PageCount = null,
    int? SlideCount = null,
    IReadOnlyList<string>? WorksheetNames = null,
    string? DetectedEncoding = null);
