namespace Lantern.Infrastructure.Search;

/// <summary>
/// Configuration options for the Meilisearch search backend.
/// </summary>
/// <remarks>
/// The API key is resolved in this priority order:
///   1. <see cref="ApiKeyFile"/> — path to a file whose sole content is the key.
///   2. The <c>MEILI_API_KEY</c> environment variable.
///   3. An empty string (anonymous access — only suitable for local dev without auth).
/// </remarks>
public sealed class MeilisearchOptions
{
    public const string SectionName = "Meilisearch";

    public string Url { get; set; } = "http://127.0.0.1:7700";
    public string IndexName { get; set; } = "lantern-documents";
    public int MaxDocumentsPerBatch { get; set; } = 100;
    public int MaxPayloadBytesPerBatch { get; set; } = 4 * 1024 * 1024;
    public int TaskTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Optional path to a file that contains the Meilisearch API key.
    /// When set and the file exists, its contents take precedence over the
    /// <c>MEILI_API_KEY</c> environment variable.
    /// </summary>
    public string ApiKeyFile { get; set; } = "";

    /// <summary>
    /// Resolves the API key using the configured file path or environment variable.
    /// </summary>
    public string ResolveApiKey()
    {
        if (!string.IsNullOrEmpty(ApiKeyFile) && File.Exists(ApiKeyFile))
            return File.ReadAllText(ApiKeyFile).Trim();
        return Environment.GetEnvironmentVariable("MEILI_API_KEY") ?? "";
    }
}
