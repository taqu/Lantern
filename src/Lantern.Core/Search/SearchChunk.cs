using System.Text.Json.Serialization;

namespace Lantern.Core.Search;

public sealed class SearchChunk
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("documentId")] public string DocumentId { get; init; } = "";
    [JsonPropertyName("sourceId")] public string SourceId { get; init; } = "";
    [JsonPropertyName("sourceType")] public string SourceType { get; init; } = "";
    [JsonPropertyName("title")] public required string Title { get; init; }
    [JsonPropertyName("content")] public required string Content { get; init; }
    [JsonPropertyName("chunkIndex")] public int ChunkIndex { get; init; }
    [JsonPropertyName("sectionTitle")] public string? SectionTitle { get; init; }
    [JsonPropertyName("pageNumber")] public int? PageNumber { get; init; }
    [JsonPropertyName("slideNumber")] public int? SlideNumber { get; init; }
    [JsonPropertyName("sheetName")] public string? SheetName { get; init; }
    [JsonPropertyName("startOffset")] public int? StartOffset { get; init; }
    [JsonPropertyName("endOffset")] public int? EndOffset { get; init; }
    [JsonPropertyName("uri")] public string Uri { get; init; } = "";
    [JsonPropertyName("extension")] public string? Extension { get; init; }
}
