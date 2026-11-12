using Lantern.Infrastructure.Search;
using Microsoft.Extensions.Options;

namespace Lantern.Web;

/// <summary>
/// <see cref="IMasterKeyAccessor"/> implementation that reads the master key
/// from the file configured on <see cref="MeilisearchProcessOptions.MasterKeyFile"/>.
/// Returns <c>null</c> when the path is not configured or the file is absent so
/// the credential manager can decide whether to fall back to its cached store.
/// </summary>
public sealed class MeilisearchMasterKeyAccessor(
    IOptions<MeilisearchProcessOptions> options) : IMasterKeyAccessor
{
    private readonly MeilisearchProcessOptions _options = options.Value;

    public async Task<string?> TryReadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.MasterKeyFile)) return null;
        if (!File.Exists(_options.MasterKeyFile)) return null;
        string content = await File.ReadAllTextAsync(_options.MasterKeyFile, cancellationToken);
        return content.Trim();
    }
}
