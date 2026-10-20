using Lantern.Core.Crawling;
using Lantern.Core.Jobs;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Lantern.Crawling.Jobs;

/// <summary>
/// Handles job submission, retrieval, cancellation, retry, and schedule management.
/// Single-instance; concurrent dispatch is handled by JobDispatcherService.
/// </summary>
public sealed class CrawlerJobService(
    ICrawlerJobRepository jobs,
    ICrawlerScheduleRepository schedules,
    ILogger<CrawlerJobService> logger)
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Submits a manual job. Returns the job ID or null if submission was rejected.
    /// </summary>
    public async Task<JobSubmissionResult> SubmitManualJobAsync(
        Guid crawlerId, string crawlerName, FileCrawlerConfiguration config, CancellationToken ct = default)
    {
        string snapshot = JsonSerializer.Serialize(config, _json);
        return await SubmitJobAsync(crawlerId, crawlerName, JobTrigger.Manual, snapshot, ct);
    }

    /// <summary>
    /// Submits a manual web crawl job. Returns the job ID or null if submission was rejected.
    /// </summary>
    public async Task<JobSubmissionResult> SubmitManualJobAsync(
        Guid crawlerId, string crawlerName, WebCrawlerConfiguration config, CancellationToken ct = default)
    {
        string snapshot = JsonSerializer.Serialize(config, _json);
        return await SubmitJobAsync(crawlerId, crawlerName, JobTrigger.Manual, snapshot, ct);
    }

    private async Task<JobSubmissionResult> SubmitJobAsync(
        Guid crawlerId, string crawlerName, JobTrigger trigger, string snapshot, CancellationToken ct)
    {
        if (await jobs.HasActiveJobForCrawlerAsync(crawlerId, ct))
        {
            logger.LogInformation("Manual submission rejected: crawler {CrawlerId} already has an active job", crawlerId);
            return new JobSubmissionResult(JobSubmissionStatus.AlreadyActive, null);
        }

        var job = new CrawlerJob
        {
            CrawlerId = crawlerId,
            CrawlerName = crawlerName,
            Trigger = trigger,
            Status = JobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            AttemptNumber = 1,
            ConfigurationSnapshot = snapshot
        };
        await jobs.CreateJobAsync(job, ct);
        await jobs.AddEventAsync(new CrawlerJobEvent { JobId = job.Id, EventType = JobEventType.JobCreated, OccurredAt = job.CreatedAt }, ct);
        logger.LogInformation("Manual job {JobId} submitted for crawler {CrawlerId}", job.Id, crawlerId);
        return new JobSubmissionResult(JobSubmissionStatus.Submitted, job.Id);
    }

    /// <summary>
    /// Creates a scheduled job. Returns false if an active job already exists (coalesced).
    /// </summary>
    public async Task<bool> SubmitScheduledJobAsync(
        Guid crawlerId, string crawlerName, FileCrawlerConfiguration config, DateTimeOffset scheduledAt, CancellationToken ct = default)
    {
        if (await jobs.HasActiveJobForCrawlerAsync(crawlerId, ct))
        {
            logger.LogDebug("Scheduled job coalesced: crawler {CrawlerId} already has an active job", crawlerId);
            return false;
        }

        string snapshot = JsonSerializer.Serialize(config, _json);
        var job = new CrawlerJob
        {
            CrawlerId = crawlerId,
            CrawlerName = crawlerName,
            Trigger = JobTrigger.Scheduled,
            Status = JobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            ScheduledAt = scheduledAt,
            AttemptNumber = 1,
            ConfigurationSnapshot = snapshot
        };
        await jobs.CreateJobAsync(job, ct);
        await jobs.AddEventAsync(new CrawlerJobEvent { JobId = job.Id, EventType = JobEventType.JobCreated, OccurredAt = job.CreatedAt, Details = $"Scheduled at {scheduledAt:u}" }, ct);
        logger.LogInformation("Scheduled job {JobId} created for crawler {CrawlerId}", job.Id, crawlerId);
        return true;
    }

    public async Task<bool> RequestCancellationAsync(Guid jobId, CancellationToken ct = default)
    {
        bool updated = await jobs.RequestCancellationAsync(jobId, ct);
        if (updated)
        {
            await jobs.AddEventAsync(new CrawlerJobEvent { JobId = jobId, EventType = JobEventType.CancellationRequested, OccurredAt = DateTimeOffset.UtcNow }, ct);
            logger.LogInformation("Cancellation requested for job {JobId}", jobId);
        }
        return updated;
    }

    /// <summary>
    /// Creates a retry job linked to the original. Returns null if not retryable.
    /// </summary>
    public async Task<JobSubmissionResult> RetryJobAsync(
        Guid originalJobId, string crawlerName, FileCrawlerConfiguration? newConfig, CancellationToken ct = default)
    {
        CrawlerJob? original = await jobs.GetJobAsync(originalJobId, ct);
        if (original == null) return new JobSubmissionResult(JobSubmissionStatus.NotFound, null);
        if (original.Status is not (JobStatus.Failed or JobStatus.Canceled))
            return new JobSubmissionResult(JobSubmissionStatus.NotRetryable, null);
        if (await jobs.HasActiveJobForCrawlerAsync(original.CrawlerId, ct))
            return new JobSubmissionResult(JobSubmissionStatus.AlreadyActive, null);

        string snapshot = newConfig != null
            ? JsonSerializer.Serialize(newConfig, _json)
            : original.ConfigurationSnapshot ?? JsonSerializer.Serialize(new FileCrawlerConfiguration(), _json);

        var retry = new CrawlerJob
        {
            CrawlerId = original.CrawlerId,
            CrawlerName = original.CrawlerName,
            Trigger = JobTrigger.Retry,
            Status = JobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            AttemptNumber = original.AttemptNumber + 1,
            ConfigurationSnapshot = snapshot,
            RetryOfJobId = originalJobId
        };
        await jobs.CreateJobAsync(retry, ct);
        await jobs.AddEventAsync(new CrawlerJobEvent { JobId = retry.Id, EventType = JobEventType.JobRetried, OccurredAt = retry.CreatedAt, Details = $"Retry of {originalJobId}" }, ct);
        logger.LogInformation("Retry job {JobId} created for original {OriginalJobId}", retry.Id, originalJobId);
        return new JobSubmissionResult(JobSubmissionStatus.Submitted, retry.Id);
    }

    public Task<CrawlerJob?> GetJobAsync(Guid jobId, CancellationToken ct = default) =>
        jobs.GetJobAsync(jobId, ct);

    public Task<(IReadOnlyList<CrawlerJob> Items, int TotalCount)> GetJobsPagedAsync(
        int page, int pageSize, Guid? crawlerId = null, JobStatus? status = null, CancellationToken ct = default) =>
        jobs.GetJobsPagedAsync(page, pageSize, crawlerId, status, ct);

    public Task<CrawlerJob?> GetActiveJobForCrawlerAsync(Guid crawlerId, CancellationToken ct = default) =>
        jobs.GetActiveJobForCrawlerAsync(crawlerId, ct);

    public Task<IReadOnlyList<CrawlerJobEvent>> GetJobEventsAsync(Guid jobId, CancellationToken ct = default) =>
        jobs.GetJobEventsAsync(jobId, ct);

    public Task<CrawlerSchedule?> GetScheduleAsync(Guid crawlerId, CancellationToken ct = default) =>
        schedules.GetScheduleAsync(crawlerId, ct);

    public async Task SaveScheduleAsync(CrawlerSchedule schedule, CancellationToken ct = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        schedule.NextRunAt = JobScheduleCalculator.CalculateInitialNextRun(schedule, now);
        await schedules.SaveScheduleAsync(schedule, ct);
        logger.LogInformation("Schedule saved for crawler {CrawlerId}: type={Type}, next={Next}",
            schedule.CrawlerId, schedule.Type, schedule.NextRunAt);
    }

    public Task DeleteScheduleAsync(Guid crawlerId, CancellationToken ct = default) =>
        schedules.DeleteScheduleAsync(crawlerId, ct);

    public static FileCrawlerConfiguration? ParseSnapshot(string? snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot)) return null;
        try { return JsonSerializer.Deserialize<FileCrawlerConfiguration>(snapshot, _json); }
        catch { return null; }
    }

    public static WebCrawlerConfiguration? ParseWebSnapshot(string? snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot)) return null;
        try { return JsonSerializer.Deserialize<WebCrawlerConfiguration>(snapshot, _json); }
        catch { return null; }
    }

    public static string DetectCrawlerType(string? snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot)) return "file";
        try
        {
            using var doc = JsonDocument.Parse(snapshot);
            if (doc.RootElement.TryGetProperty("crawlerType", out var ct))
                return ct.GetString() ?? "file";
        }
        catch { }
        return "file";
    }

    /// <summary>
    /// Creates a scheduled web crawl job. Returns false if an active job already exists (coalesced).
    /// </summary>
    public async Task<bool> SubmitScheduledWebJobAsync(
        Guid crawlerId, string crawlerName, WebCrawlerConfiguration config, DateTimeOffset scheduledAt, CancellationToken ct = default)
    {
        if (await jobs.HasActiveJobForCrawlerAsync(crawlerId, ct))
        {
            logger.LogDebug("Scheduled job coalesced: crawler {CrawlerId} already has an active job", crawlerId);
            return false;
        }

        string snapshot = JsonSerializer.Serialize(config, _json);
        var job = new CrawlerJob
        {
            CrawlerId = crawlerId,
            CrawlerName = crawlerName,
            Trigger = JobTrigger.Scheduled,
            Status = JobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            ScheduledAt = scheduledAt,
            AttemptNumber = 1,
            ConfigurationSnapshot = snapshot
        };
        await jobs.CreateJobAsync(job, ct);
        await jobs.AddEventAsync(new CrawlerJobEvent { JobId = job.Id, EventType = JobEventType.JobCreated, OccurredAt = job.CreatedAt, Details = $"Scheduled at {scheduledAt:u}" }, ct);
        logger.LogInformation("Scheduled web job {JobId} created for crawler {CrawlerId}", job.Id, crawlerId);
        return true;
    }
}

public sealed record JobSubmissionResult(JobSubmissionStatus Status, Guid? JobId);

public enum JobSubmissionStatus
{
    Submitted,
    AlreadyActive,
    Disabled,
    InvalidConfiguration,
    NotFound,
    NotRetryable
}
