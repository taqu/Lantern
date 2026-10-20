using System.Text.Json.Serialization;

namespace Lantern.Core.Search;

/// <summary>
/// Represents a document that can be indexed and searched.
/// </summary>
public sealed class SearchDocument
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("sourceId")] public string SourceId { get; init; } = "";
    [JsonPropertyName("sourceType")] public string SourceType { get; init; } = "";
    [JsonPropertyName("uri")] public string Uri { get; init; } = "";
    [JsonPropertyName("path")] public string? Path { get; init; }
    [JsonPropertyName("extension")] public string? Extension { get; init; }
    [JsonPropertyName("mimeType")] public string? MimeType { get; init; }
    [JsonPropertyName("size")] public long? Size { get; init; }
    [JsonPropertyName("modifiedAt")] public DateTimeOffset? ModifiedAt { get; init; }
    [JsonPropertyName("title")] public required string Title { get; init; }
    [JsonPropertyName("content")] public required string Content { get; init; }
    // Compatibility with Phase 0 callers; new writes use the normalized schema.
    [JsonIgnore] public string? SourcePath { get; init; }
    [JsonIgnore] public string? Url { get; init; }
    [JsonIgnore] public string? DocumentType { get; init; }
    [JsonPropertyName("indexedAt")] public DateTimeOffset IndexedAt { get; init; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("categoryIds")] public string[] CategoryIds { get; init; } = [];
}
