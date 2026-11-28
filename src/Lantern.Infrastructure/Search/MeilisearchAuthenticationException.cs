using Meilisearch;

namespace Lantern.Infrastructure.Search;

/// <summary>
/// Thrown when Meilisearch rejects a request due to a missing or invalid API key.
/// This is a configuration or credential failure, not a per-document extraction failure.
/// </summary>
public sealed class MeilisearchAuthenticationException : Exception
{
    public string ErrorCode { get; }

    public MeilisearchAuthenticationException(MeilisearchApiError inner)
        : base($"Meilisearch authentication failed ({inner.Code}). Check that ApiKeyFile contains a valid key registered with the running Meilisearch instance.", inner)
    {
        ErrorCode = inner.Code ?? "unknown";
    }
}
