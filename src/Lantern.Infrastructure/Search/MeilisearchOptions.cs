namespace Lantern.Infrastructure.Search;

/// <summary>
/// Configuration options for the Meilisearch search backend.
/// The API key is sourced from the MEILI_API_KEY environment variable at registration time,
/// not from this options class.
/// </summary>
public sealed class MeilisearchOptions
{
    public const string SectionName = "Meilisearch";

    public string Url { get; set; } = "http://127.0.0.1:7700";
    public string IndexName { get; set; } = "lantern-documents";
}
