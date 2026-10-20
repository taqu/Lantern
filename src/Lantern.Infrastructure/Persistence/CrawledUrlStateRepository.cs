using Lantern.Core.Crawling;
using Microsoft.EntityFrameworkCore;

namespace Lantern.Infrastructure.Persistence;

public sealed class CrawledUrlStateRepository(IDbContextFactory<LanternDbContext> factory) : ICrawledUrlStateRepository
{
    public async Task<UrlState?> FindAsync(Guid crawlerId, string canonicalUrl, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        CrawledUrlState? record = await db.CrawledUrlStates
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.CrawlerId == crawlerId && s.CanonicalUrl == canonicalUrl, ct);
        return record == null ? null : ToDomain(record);
    }

    public async Task<IReadOnlyList<UrlState>> GetNotSeenInScanAsync(Guid crawlerId, Guid scanId, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        List<CrawledUrlState> records = await db.CrawledUrlStates
            .AsNoTracking()
            .Where(s => s.CrawlerId == crawlerId && s.LastSeenScanId != scanId)
            .ToListAsync(ct);
        return records.Select(ToDomain).ToList();
    }

    public async Task SaveAsync(UrlState state, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        CrawledUrlState? existing = await db.CrawledUrlStates
            .FirstOrDefaultAsync(s => s.CrawlerId == state.CrawlerId && s.CanonicalUrl == state.CanonicalUrl, ct);

        if (existing == null)
        {
            db.CrawledUrlStates.Add(FromDomain(state));
        }
        else
        {
            existing.DocumentId = state.DocumentId;
            existing.ETag = state.ETag;
            existing.LastModified = state.LastModified;
            existing.ContentHash = state.ContentHash;
            existing.LastSeenScanId = state.LastSeenScanId;
            existing.LastFetchedAt = state.LastFetchedAt;
            existing.LastIndexedAt = state.LastIndexedAt;
            existing.LastHttpStatus = state.LastHttpStatus;
            existing.SyncStatus = state.SyncStatus.ToString();
            existing.LastError = state.LastError;
            state.Id = existing.Id;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        await db.CrawledUrlStates.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
    }

    public async Task DeleteAllForCrawlerAsync(Guid crawlerId, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        await db.CrawledUrlStates.Where(s => s.CrawlerId == crawlerId).ExecuteDeleteAsync(ct);
    }

    private static UrlState ToDomain(CrawledUrlState r) => new()
    {
        Id = r.Id,
        CrawlerId = r.CrawlerId,
        CanonicalUrl = r.CanonicalUrl,
        DocumentId = r.DocumentId,
        ETag = r.ETag,
        LastModified = r.LastModified,
        ContentHash = r.ContentHash,
        LastSeenScanId = r.LastSeenScanId,
        LastFetchedAt = r.LastFetchedAt,
        LastIndexedAt = r.LastIndexedAt,
        LastHttpStatus = r.LastHttpStatus,
        SyncStatus = Enum.TryParse<UrlSyncStatus>(r.SyncStatus, out var s) ? s : UrlSyncStatus.Pending,
        LastError = r.LastError
    };

    private static CrawledUrlState FromDomain(UrlState s) => new()
    {
        Id = s.Id,
        CrawlerId = s.CrawlerId,
        CanonicalUrl = s.CanonicalUrl,
        DocumentId = s.DocumentId,
        ETag = s.ETag,
        LastModified = s.LastModified,
        ContentHash = s.ContentHash,
        LastSeenScanId = s.LastSeenScanId,
        LastFetchedAt = s.LastFetchedAt,
        LastIndexedAt = s.LastIndexedAt,
        LastHttpStatus = s.LastHttpStatus,
        SyncStatus = s.SyncStatus.ToString(),
        LastError = s.LastError
    };
}
