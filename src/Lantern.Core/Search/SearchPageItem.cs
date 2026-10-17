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
    string? DocumentType);
