namespace Lantern.Core.Jobs;

public interface ICrawlerJobRepository
{
    Task<Guid> CreateJobAsync(CrawlerJob job, CancellationToken ct = default);
    Task<CrawlerJob?> GetJobAsync(Guid id, CancellationToken ct = default);

    // Atomically claims a Pending job as Running, enforcing per-crawler exclusion.
    // Returns true only if exactly one row was updated (claim succeeded).
    Task<bool> ClaimJobAsync(Guid jobId, DateTimeOffset startedAt, CancellationToken ct = default);

    Task<bool> RequestCancellationAsync(Guid jobId, CancellationToken ct = default);
    Task UpdateProgressAsync(Guid jobId, JobProgress progress, CancellationToken ct = default);
    Task CompleteJobAsync(Guid jobId, JobStatus status, JobProgress progress, string? errorMessage, DateTimeOffset finishedAt, CancellationToken ct = default);

    Task<IReadOnlyList<CrawlerJob>> GetPendingJobsAsync(int maxCount, CancellationToken ct = default);
    Task<bool> HasActiveJobForCrawlerAsync(Guid crawlerId, CancellationToken ct = default);
    Task<CrawlerJob?> GetActiveJobForCrawlerAsync(Guid crawlerId, CancellationToken ct = default);

    Task<(IReadOnlyList<CrawlerJob> Items, int TotalCount)> GetJobsPagedAsync(
        int page, int pageSize,
        Guid? crawlerId = null,
        JobStatus? status = null,
        CancellationToken ct = default);

    // On startup: marks orphaned Running jobs as Failed with the given reason.
    Task MarkInterruptedJobsFailedAsync(string reason, CancellationToken ct = default);

    Task<int> DeleteOldJobsAsync(DateTimeOffset olderThan, CancellationToken ct = default);

    Task AddEventAsync(CrawlerJobEvent evt, CancellationToken ct = default);
    Task<IReadOnlyList<CrawlerJobEvent>> GetJobEventsAsync(Guid jobId, CancellationToken ct = default);
    Task<int> DeleteOldEventsAsync(DateTimeOffset olderThan, CancellationToken ct = default);
}
