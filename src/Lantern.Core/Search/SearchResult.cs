namespace Lantern.Core.Search;

/// <summary>
/// Represents the result of a full-text search query.
/// </summary>
public sealed class SearchResult
{
    public required string IndexUid { get; init; }
    public required IReadOnlyList<SearchDocument> Hits { get; init; }
    public int TotalHits { get; init; }
    public long ProcessingTimeMs { get; init; }
    public required string Query { get; init; }
}
