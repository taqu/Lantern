namespace Lantern.Core.Search;

/// <summary>
/// Represents a document that can be indexed and searched.
/// </summary>
public sealed class SearchDocument
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }
    public string? SourcePath { get; init; }
    public DateTimeOffset IndexedAt { get; init; } = DateTimeOffset.UtcNow;
}
