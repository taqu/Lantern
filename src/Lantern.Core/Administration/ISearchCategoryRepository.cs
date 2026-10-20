namespace Lantern.Core.Administration;

public interface ISearchCategoryRepository
{
    Task<IReadOnlyList<SearchCategory>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SearchCategory>> GetEnabledAsync(CancellationToken ct = default);
    Task<SearchCategory?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<SearchCategory?> GetByKeyAsync(string key, CancellationToken ct = default);
    Task<bool> KeyExistsAsync(string key, int? excludeId, CancellationToken ct = default);
    Task CreateAsync(SearchCategory category, CancellationToken ct = default);
    Task UpdateAsync(SearchCategory category, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetCategoryKeysForCrawlerAsync(Guid crawlerId, CancellationToken ct = default);
    Task SetCrawlerCategoriesAsync(Guid crawlerId, IReadOnlyList<int> categoryIds, CancellationToken ct = default);
    Task<int> CountCrawlersInCategoryAsync(int categoryId, CancellationToken ct = default);
    Task<Dictionary<Guid, List<SearchCategory>>> GetCrawlerCategoriesMapAsync(CancellationToken ct = default);
}
