namespace Lantern.Infrastructure.Search;

/// <summary>
/// Reads the Meilisearch master key from wherever the host has configured it.
/// This indirection keeps <see cref="MeilisearchCredentialManager"/> decoupled
/// from the process-manager / platform-directory configuration that lives in
/// the Web layer.
/// </summary>
public interface IMasterKeyAccessor
{
    /// <summary>
    /// Returns the master key value, or <c>null</c> when it is unavailable
    /// (for example when Lantern targets an externally-managed Meilisearch
    /// instance and no master key file has been configured).
    /// </summary>
    Task<string?> TryReadAsync(CancellationToken cancellationToken);
}
