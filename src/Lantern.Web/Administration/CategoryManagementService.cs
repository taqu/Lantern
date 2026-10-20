using Lantern.Core.Administration;

namespace Lantern.Web.Administration;

public sealed class CategoryManagementService(
    ISearchCategoryRepository categories)
{
    public Task<IReadOnlyList<SearchCategory>> GetAllAsync(CancellationToken ct = default)
        => categories.GetAllAsync(ct);

    public Task<IReadOnlyList<SearchCategory>> GetEnabledAsync(CancellationToken ct = default)
        => categories.GetEnabledAsync(ct);

    public Task<SearchCategory?> GetByIdAsync(int id, CancellationToken ct = default)
        => categories.GetByIdAsync(id, ct);

    public Task<bool> KeyExistsAsync(string key, int? excludeId, CancellationToken ct = default)
        => categories.KeyExistsAsync(key, excludeId, ct);

    public Task CreateAsync(SearchCategory category, CancellationToken ct = default)
        => categories.CreateAsync(category, ct);

    public Task UpdateAsync(SearchCategory category, CancellationToken ct = default)
        => categories.UpdateAsync(category, ct);

    public Task DeleteAsync(int id, CancellationToken ct = default)
        => categories.DeleteAsync(id, ct);

    public Task<int> CountCrawlersAsync(int categoryId, CancellationToken ct = default)
        => categories.CountCrawlersInCategoryAsync(categoryId, ct);

    public Task<IReadOnlyList<string>> GetCategoryKeysForCrawlerAsync(Guid crawlerId, CancellationToken ct = default)
        => categories.GetCategoryKeysForCrawlerAsync(crawlerId, ct);

    public Task SetCrawlerCategoriesAsync(Guid crawlerId, IReadOnlyList<int> categoryIds, CancellationToken ct = default)
        => categories.SetCrawlerCategoriesAsync(crawlerId, categoryIds, ct);
}
