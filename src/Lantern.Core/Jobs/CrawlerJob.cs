namespace Lantern.Core.Jobs;

public sealed class CrawlerJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CrawlerId { get; set; }
    public string CrawlerName { get; set; } = "";
    public JobTrigger Trigger { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public int AttemptNumber { get; set; } = 1;
    public string? ConfigurationSnapshot { get; set; }
    public string? ErrorMessage { get; set; }
    public bool CancellationRequested { get; set; }
    public Guid? RetryOfJobId { get; set; }
    public JobProgress Progress { get; set; } = JobProgress.Empty;

    public bool IsActive => Status is JobStatus.Pending or JobStatus.Running;

    public TimeSpan? Duration => FinishedAt.HasValue && StartedAt.HasValue
        ? FinishedAt.Value - StartedAt.Value
        : StartedAt.HasValue
            ? DateTimeOffset.UtcNow - StartedAt.Value
            : null;
}
