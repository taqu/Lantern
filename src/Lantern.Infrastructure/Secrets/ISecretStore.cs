namespace Lantern.Infrastructure.Secrets;

/// <summary>
/// Cross-platform abstraction for reading and writing secret values by logical
/// name. The physical storage layout (plaintext file, DPAPI-protected blob,
/// keychain, etc.) is an implementation detail. Secret names must be opaque,
/// filesystem-safe identifiers chosen by the caller.
/// </summary>
public interface ISecretStore
{
    /// <summary>
    /// Returns the stored value for <paramref name="name"/> or <c>null</c> if
    /// no secret has been written under that name.
    /// </summary>
    Task<string?> TryReadAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Persists <paramref name="value"/> under <paramref name="name"/>. Any
    /// previously stored value is replaced atomically where the implementation
    /// supports it.
    /// </summary>
    Task WriteAsync(string name, string value, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a stored secret. No-op when the secret is already absent.
    /// </summary>
    Task DeleteAsync(string name, CancellationToken cancellationToken);
}
