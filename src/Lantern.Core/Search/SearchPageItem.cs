namespace Lantern.Core.Search;

/// <summary>
/// Represents a single search result for display in the UI.
/// </summary>
public sealed record SearchPageItem(
    string Id,
    string Title,
    string? Url,
    string? Path,
    string? Snippet,
    string? DocumentType,
    string? SourceId = null,
    string? SourceType = null,
    string? Extension = null,
    string? MimeType = null,
    long? Size = null,
    DateTimeOffset? ModifiedAt = null,
    DateTimeOffset? IndexedAt = null,
    string[]? CategoryIds = null);
