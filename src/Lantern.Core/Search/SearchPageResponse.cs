namespace Lantern.Core.Search;

/// <summary>
/// Represents a paginated search response for display in the UI.
/// </summary>
public sealed record SearchPageResponse(
    IReadOnlyList<SearchPageItem> Items,
    long TotalHits,
    int Page,
    int PageSize,
    string Query);
