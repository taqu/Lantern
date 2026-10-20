using Lantern.Core.Administration;

namespace Lantern.Infrastructure.Persistence;

public sealed class CrawlerCategoryResolver(ISearchCategoryRepository categories) : ICrawlerCategoryResolver
{
    public async Task<string[]> GetCategoryKeysAsync(Guid crawlerId, CancellationToken ct = default)
    {
        IReadOnlyList<string> keys = await categories.GetCategoryKeysForCrawlerAsync(crawlerId, ct);
        return keys.ToArray();
    }
}
