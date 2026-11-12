namespace Lantern.Infrastructure.Search;

/// <summary>
/// A resolved Meilisearch credential for a specific <see cref="MeilisearchCredentialRole"/>.
/// Produced by <see cref="IMeilisearchCredentialManager"/> after provisioning or validation.
/// </summary>
/// <param name="Role">The logical role this credential fulfills.</param>
/// <param name="KeyValue">The opaque API key value sent as the <c>Authorization: Bearer</c> token.</param>
/// <param name="KeyUid">The Meilisearch-assigned UID of the key (used for revocation and status).</param>
/// <param name="ExpiresAt">Absolute expiration, or null for a non-expiring key.</param>
public sealed record MeilisearchCredential(
    MeilisearchCredentialRole Role,
    string KeyValue,
    string? KeyUid,
    DateTime? ExpiresAt);
