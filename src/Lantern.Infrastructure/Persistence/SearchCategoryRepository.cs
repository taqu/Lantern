using Lantern.Core.Administration;
using Microsoft.EntityFrameworkCore;

namespace Lantern.Infrastructure.Persistence;

public sealed class SearchCategoryRepository(IDbContextFactory<LanternDbContext> factory) : ISearchCategoryRepository
{
    public async Task<IReadOnlyList<SearchCategory>> GetAllAsync(CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        return await db.SearchCategories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => c.ToDomain()).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SearchCategory>> GetEnabledAsync(CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        return await db.SearchCategories.AsNoTracking().Where(c => c.Enabled)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => c.ToDomain()).ToListAsync(ct);
    }

    public async Task<SearchCategory?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        SearchCategoryRecord? r = await db.SearchCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        return r?.ToDomain();
    }

    public async Task<SearchCategory?> GetByKeyAsync(string key, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        SearchCategoryRecord? r = await db.SearchCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Key == key, ct);
        return r?.ToDomain();
    }

    public async Task<bool> KeyExistsAsync(string key, int? excludeId, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        return await db.SearchCategories.AnyAsync(c => c.Key == key && (excludeId == null || c.Id != excludeId.Value), ct);
    }

    public async Task CreateAsync(SearchCategory category, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        category.Validate();
        category.CreatedAt = DateTimeOffset.UtcNow;
        category.UpdatedAt = DateTimeOffset.UtcNow;
        SearchCategoryRecord record = SearchCategoryRecord.FromDomain(category);
        db.SearchCategories.Add(record);
        await db.SaveChangesAsync(ct);
        category.Id = record.Id;
    }

    public async Task UpdateAsync(SearchCategory category, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        category.Validate();
        SearchCategoryRecord record = await db.SearchCategories.FirstAsync(c => c.Id == category.Id, ct);
        record.Key = category.Key;
        record.Name = category.Name;
        record.Description = category.Description;
        record.SortOrder = category.SortOrder;
        record.Enabled = category.Enabled;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        SearchCategoryRecord? record = await db.SearchCategories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (record != null)
        {
            List<CrawlerCategoryRecord> assignments = await db.CrawlerCategories.Where(cc => cc.CategoryId == id).ToListAsync(ct);
            db.CrawlerCategories.RemoveRange(assignments);
            db.SearchCategories.Remove(record);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<IReadOnlyList<string>> GetCategoryKeysForCrawlerAsync(Guid crawlerId, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        return await db.CrawlerCategories
            .Where(cc => cc.CrawlerId == crawlerId)
            .Join(db.SearchCategories, cc => cc.CategoryId, c => c.Id, (cc, c) => c.Key)
            .ToListAsync(ct);
    }

    public async Task SetCrawlerCategoriesAsync(Guid crawlerId, IReadOnlyList<int> categoryIds, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        List<CrawlerCategoryRecord> existing = await db.CrawlerCategories.Where(cc => cc.CrawlerId == crawlerId).ToListAsync(ct);
        db.CrawlerCategories.RemoveRange(existing);
        foreach (int catId in categoryIds.Distinct())
            db.CrawlerCategories.Add(new CrawlerCategoryRecord { CrawlerId = crawlerId, CategoryId = catId });
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> CountCrawlersInCategoryAsync(int categoryId, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        return await db.CrawlerCategories.CountAsync(cc => cc.CategoryId == categoryId, ct);
    }

    public async Task<Dictionary<Guid, List<SearchCategory>>> GetCrawlerCategoriesMapAsync(CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        var assignments = await db.CrawlerCategories
            .Join(db.SearchCategories, cc => cc.CategoryId, c => c.Id, (cc, c) => new { cc.CrawlerId, Category = c })
            .AsNoTracking()
            .ToListAsync(ct);
        return assignments.GroupBy(a => a.CrawlerId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.Category.ToDomain()).ToList());
    }
}
