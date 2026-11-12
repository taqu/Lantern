namespace Lantern.Infrastructure.Search;

/// <summary>
/// Owns the lifecycle of Meilisearch API keys for each
/// <see cref="MeilisearchCredentialRole"/>: discovery, validation,
/// first-time provisioning through the Meilisearch <c>/keys</c> API, and
/// in-memory availability for the <see cref="IMeilisearchClientFactory"/>.
/// </summary>
public interface IMeilisearchCredentialManager
{
    /// <summary>
    /// Ensures every required credential is available by reusing valid existing
    /// keys from the configured file paths or by creating new scoped keys via
    /// the master key. Must be called before any role's credential is requested.
    /// Safe to call repeatedly; the operation is idempotent per role.
    /// </summary>
    Task EnsureCredentialsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns the credential previously resolved by <see cref="EnsureCredentialsAsync"/>.
    /// Throws <see cref="InvalidOperationException"/> if the credential is not yet available.
    /// </summary>
    MeilisearchCredential GetCredential(MeilisearchCredentialRole role);

    /// <summary>
    /// True when <see cref="EnsureCredentialsAsync"/> has produced valid credentials
    /// for every required role; false before first ensure or when provisioning failed.
    /// </summary>
    bool IsReady { get; }
}
