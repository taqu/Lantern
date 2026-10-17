namespace Lantern.Core.Search;

/// <summary>
/// Provides validation and normalization helpers for search queries.
/// </summary>
public static class SearchQueryValidator
{
    public const int MaxQueryLength = 500;

    public static bool IsValid(string? query) =>
        !string.IsNullOrWhiteSpace(query) && query.Length <= MaxQueryLength;

    public static int NormalizePage(int page) => page < 1 ? 1 : page;

    public static int CalculateOffset(int page, int pageSize) =>
        Math.Max(0, (NormalizePage(page) - 1) * pageSize);
}
