namespace Lantern.Core.Administration;

/// <summary>
/// Resolves current category keys for a crawler at indexing time.
/// </summary>
public interface ICrawlerCategoryResolver
{
    Task<string[]> GetCategoryKeysAsync(Guid crawlerId, CancellationToken ct = default);
}
