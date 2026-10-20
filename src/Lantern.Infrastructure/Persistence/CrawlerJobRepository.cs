using Lantern.Core.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Lantern.Infrastructure.Persistence;

public sealed class CrawlerJobRepository(IDbContextFactory<LanternDbContext> factory) : ICrawlerJobRepository
{
    public async Task<Guid> CreateJobAsync(CrawlerJob job, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        db.CrawlerJobs.Add(CrawlerJobRecord.FromDomain(job));
        await db.SaveChangesAsync(ct);
        return job.Id;
    }

    public async Task<CrawlerJob?> GetJobAsync(Guid id, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        CrawlerJobRecord? rec = await db.CrawlerJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, ct);
        return rec?.ToDomain();
    }

    // Atomically claims a Pending job as Running, enforcing per-crawler exclusion via a single SQL statement.
    // SQLite serializes writes, so concurrent dispatchers within one process are safe.
    public async Task<bool> ClaimJobAsync(Guid jobId, DateTimeOffset startedAt, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        int affected = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE CrawlerJobs
            SET Status = 'Running', StartedAt = {startedAt}
            WHERE Id = {jobId}
              AND Status = 'Pending'
              AND NOT EXISTS (
                SELECT 1 FROM CrawlerJobs AS r
                WHERE r.CrawlerId = (SELECT CrawlerId FROM CrawlerJobs WHERE Id = {jobId})
                  AND r.Status = 'Running'
              )
            """, ct);
        return affected == 1;
    }

    public async Task<bool> RequestCancellationAsync(Guid jobId, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        int affected = await db.CrawlerJobs
            .Where(j => j.Id == jobId && (j.Status == "Pending" || j.Status == "Running"))
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.CancellationRequested, true), ct);
        return affected == 1;
    }

    public async Task UpdateProgressAsync(Guid jobId, JobProgress progress, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        await db.CrawlerJobs
            .Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.DiscoveredFiles, progress.DiscoveredFiles)
                .SetProperty(j => j.ProcessedFiles, progress.ProcessedFiles)
                .SetProperty(j => j.IndexedFiles, progress.IndexedFiles)
                .SetProperty(j => j.UnchangedFiles, progress.UnchangedFiles)
                .SetProperty(j => j.SkippedFiles, progress.SkippedFiles)
                .SetProperty(j => j.FailedFiles, progress.FailedFiles)
                .SetProperty(j => j.DeletedFiles, progress.DeletedFiles),
                ct);
    }

    public async Task CompleteJobAsync(Guid jobId, JobStatus status, JobProgress progress, string? errorMessage, DateTimeOffset finishedAt, CancellationToken ct = default)
    {
        string statusStr = status.ToString();
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        await db.CrawlerJobs
            .Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, statusStr)
                .SetProperty(j => j.FinishedAt, finishedAt)
                .SetProperty(j => j.ErrorMessage, errorMessage)
                .SetProperty(j => j.DiscoveredFiles, progress.DiscoveredFiles)
                .SetProperty(j => j.ProcessedFiles, progress.ProcessedFiles)
                .SetProperty(j => j.IndexedFiles, progress.IndexedFiles)
                .SetProperty(j => j.UnchangedFiles, progress.UnchangedFiles)
                .SetProperty(j => j.SkippedFiles, progress.SkippedFiles)
                .SetProperty(j => j.FailedFiles, progress.FailedFiles)
                .SetProperty(j => j.DeletedFiles, progress.DeletedFiles),
                ct);
    }

    public async Task<IReadOnlyList<CrawlerJob>> GetPendingJobsAsync(int maxCount, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        List<CrawlerJobRecord> recs = await db.CrawlerJobs.AsNoTracking()
            .Where(j => j.Status == "Pending")
            .OrderBy(j => j.CreatedAt)
            .Take(maxCount)
            .ToListAsync(ct);
        return recs.Select(r => r.ToDomain()).ToList();
    }

    public async Task<bool> HasActiveJobForCrawlerAsync(Guid crawlerId, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        return await db.CrawlerJobs.AsNoTracking()
            .AnyAsync(j => j.CrawlerId == crawlerId && (j.Status == "Pending" || j.Status == "Running"), ct);
    }

    public async Task<CrawlerJob?> GetActiveJobForCrawlerAsync(Guid crawlerId, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        CrawlerJobRecord? rec = await db.CrawlerJobs.AsNoTracking()
            .Where(j => j.CrawlerId == crawlerId && (j.Status == "Pending" || j.Status == "Running"))
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return rec?.ToDomain();
    }

    public async Task<(IReadOnlyList<CrawlerJob> Items, int TotalCount)> GetJobsPagedAsync(
        int page, int pageSize, Guid? crawlerId = null, JobStatus? status = null, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        IQueryable<CrawlerJobRecord> query = db.CrawlerJobs.AsNoTracking();
        if (crawlerId.HasValue) query = query.Where(j => j.CrawlerId == crawlerId.Value);
        if (status.HasValue) { string s = status.Value.ToString(); query = query.Where(j => j.Status == s); }
        int total = await query.CountAsync(ct);
        List<CrawlerJobRecord> recs = await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (recs.Select(r => r.ToDomain()).ToList(), total);
    }

    public async Task MarkInterruptedJobsFailedAsync(string reason, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await db.CrawlerJobs
            .Where(j => j.Status == "Running")
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, "Failed")
                .SetProperty(j => j.FinishedAt, now)
                .SetProperty(j => j.ErrorMessage, reason),
                ct);
        // Also mark Pending+CancellationRequested as Canceled
        await db.CrawlerJobs
            .Where(j => j.Status == "Pending" && j.CancellationRequested)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, "Canceled")
                .SetProperty(j => j.FinishedAt, now)
                .SetProperty(j => j.ErrorMessage, "Canceled before execution began"),
                ct);
    }

    public async Task<int> DeleteOldJobsAsync(DateTimeOffset olderThan, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        // Only delete terminal, non-active jobs
        return await db.CrawlerJobs
            .Where(j => j.CreatedAt < olderThan
                     && (j.Status == "Completed" || j.Status == "Failed" || j.Status == "Canceled"))
            .ExecuteDeleteAsync(ct);
    }

    public async Task AddEventAsync(CrawlerJobEvent evt, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        db.CrawlerJobEvents.Add(CrawlerJobEventRecord.FromDomain(evt));
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CrawlerJobEvent>> GetJobEventsAsync(Guid jobId, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        List<CrawlerJobEventRecord> recs = await db.CrawlerJobEvents.AsNoTracking()
            .Where(e => e.JobId == jobId)
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(ct);
        return recs.Select(r => r.ToDomain()).ToList();
    }

    public async Task<int> DeleteOldEventsAsync(DateTimeOffset olderThan, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        return await db.CrawlerJobEvents
            .Where(e => e.OccurredAt < olderThan)
            .ExecuteDeleteAsync(ct);
    }
}
