using Lantern.Core.Crawling;
using Microsoft.EntityFrameworkCore;

namespace Lantern.Infrastructure.Persistence;

public sealed class CrawlStateRepository(IDbContextFactory<LanternDbContext> factory) : ICrawlStateRepository
{
    public async Task<FileState?> FindAsync(Guid crawlerId, string canonicalPath, CancellationToken cancellationToken = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        CrawledFileState? entity = await db.CrawledFileStates
            .Where(e => e.CrawlerId == crawlerId && e.CanonicalPath == canonicalPath)
            .FirstOrDefaultAsync(cancellationToken);
        return entity == null ? null : ToDto(entity);
    }

    public async Task<IReadOnlyList<FileState>> GetNotSeenInScanAsync(Guid crawlerId, Guid scanId, CancellationToken cancellationToken = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        List<CrawledFileState> entities = await db.CrawledFileStates
            .Where(e => e.CrawlerId == crawlerId && e.LastSeenScanId != scanId && e.SyncStatus == nameof(CrawlSyncStatus.Indexed))
            .ToListAsync(cancellationToken);
        return entities.ConvertAll(ToDto);
    }

    public async Task SaveAsync(FileState state, CancellationToken cancellationToken = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        if (state.Id == 0)
        {
            CrawledFileState? existing = await db.CrawledFileStates
                .Where(e => e.CrawlerId == state.CrawlerId && e.CanonicalPath == state.CanonicalPath)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing != null)
            {
                ApplyToEntity(state, existing);
                await db.SaveChangesAsync(cancellationToken);
                state.Id = existing.Id;
                return;
            }
            CrawledFileState entity = ToEntity(state);
            db.CrawledFileStates.Add(entity);
            await db.SaveChangesAsync(cancellationToken);
            state.Id = entity.Id;
            return;
        }
        CrawledFileState? tracked = await db.CrawledFileStates.FindAsync([state.Id], cancellationToken);
        if (tracked == null) throw new InvalidOperationException($"CrawledFileState {state.Id} not found.");
        ApplyToEntity(state, tracked);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await db.CrawledFileStates.Where(e => e.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    private static FileState ToDto(CrawledFileState e) => new()
    {
        Id = e.Id,
        CrawlerId = e.CrawlerId,
        DocumentId = e.DocumentId,
        CanonicalPath = e.CanonicalPath,
        FileSize = e.FileSize,
        LastWriteTimeUtc = e.LastWriteTimeUtc,
        LastSeenScanId = e.LastSeenScanId,
        LastIndexedAt = e.LastIndexedAt,
        SyncStatus = Enum.Parse<CrawlSyncStatus>(e.SyncStatus),
        LastError = e.LastError
    };

    private static CrawledFileState ToEntity(FileState s) => new()
    {
        CrawlerId = s.CrawlerId,
        DocumentId = s.DocumentId,
        CanonicalPath = s.CanonicalPath,
        FileSize = s.FileSize,
        LastWriteTimeUtc = s.LastWriteTimeUtc,
        LastSeenScanId = s.LastSeenScanId,
        LastIndexedAt = s.LastIndexedAt,
        SyncStatus = s.SyncStatus.ToString(),
        LastError = s.LastError
    };

    private static void ApplyToEntity(FileState s, CrawledFileState e)
    {
        e.DocumentId = s.DocumentId;
        e.FileSize = s.FileSize;
        e.LastWriteTimeUtc = s.LastWriteTimeUtc;
        e.LastSeenScanId = s.LastSeenScanId;
        e.LastIndexedAt = s.LastIndexedAt;
        e.SyncStatus = s.SyncStatus.ToString();
        e.LastError = s.LastError;
    }
}
