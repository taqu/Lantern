namespace Lantern.Core.Search;

public sealed record SearchFilters(
    string? CategoryKey = null,
    string? SourceType = null,
    string? Extension = null)
{
    public static readonly SearchFilters Empty = new();
}
