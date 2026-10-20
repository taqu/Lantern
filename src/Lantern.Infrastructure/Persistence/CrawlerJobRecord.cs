using Lantern.Core.Jobs;

namespace Lantern.Infrastructure.Persistence;

public sealed class CrawlerJobRecord
{
    public Guid Id { get; set; }
    public Guid CrawlerId { get; set; }
    public string CrawlerName { get; set; } = "";
    public string Trigger { get; set; } = "Manual";
    public string Status { get; set; } = "Pending";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public int AttemptNumber { get; set; } = 1;
    public string? ConfigurationSnapshot { get; set; }
    public string? ErrorMessage { get; set; }
    public bool CancellationRequested { get; set; }
    public Guid? RetryOfJobId { get; set; }
    public long DiscoveredFiles { get; set; }
    public long ProcessedFiles { get; set; }
    public long IndexedFiles { get; set; }
    public long UnchangedFiles { get; set; }
    public long SkippedFiles { get; set; }
    public long FailedFiles { get; set; }
    public long DeletedFiles { get; set; }

    public CrawlerJob ToDomain() => new()
    {
        Id = Id,
        CrawlerId = CrawlerId,
        CrawlerName = CrawlerName,
        Trigger = Enum.Parse<JobTrigger>(Trigger),
        Status = Enum.Parse<JobStatus>(Status),
        CreatedAt = CreatedAt,
        ScheduledAt = ScheduledAt,
        StartedAt = StartedAt,
        FinishedAt = FinishedAt,
        AttemptNumber = AttemptNumber,
        ConfigurationSnapshot = ConfigurationSnapshot,
        ErrorMessage = ErrorMessage,
        CancellationRequested = CancellationRequested,
        RetryOfJobId = RetryOfJobId,
        Progress = new JobProgress
        {
            DiscoveredFiles = DiscoveredFiles,
            ProcessedFiles = ProcessedFiles,
            IndexedFiles = IndexedFiles,
            UnchangedFiles = UnchangedFiles,
            SkippedFiles = SkippedFiles,
            FailedFiles = FailedFiles,
            DeletedFiles = DeletedFiles
        }
    };

    public static CrawlerJobRecord FromDomain(CrawlerJob job) => new()
    {
        Id = job.Id,
        CrawlerId = job.CrawlerId,
        CrawlerName = job.CrawlerName,
        Trigger = job.Trigger.ToString(),
        Status = job.Status.ToString(),
        CreatedAt = job.CreatedAt,
        ScheduledAt = job.ScheduledAt,
        StartedAt = job.StartedAt,
        FinishedAt = job.FinishedAt,
        AttemptNumber = job.AttemptNumber,
        ConfigurationSnapshot = job.ConfigurationSnapshot,
        ErrorMessage = job.ErrorMessage,
        CancellationRequested = job.CancellationRequested,
        RetryOfJobId = job.RetryOfJobId,
        DiscoveredFiles = job.Progress.DiscoveredFiles,
        ProcessedFiles = job.Progress.ProcessedFiles,
        IndexedFiles = job.Progress.IndexedFiles,
        UnchangedFiles = job.Progress.UnchangedFiles,
        SkippedFiles = job.Progress.SkippedFiles,
        FailedFiles = job.Progress.FailedFiles,
        DeletedFiles = job.Progress.DeletedFiles
    };
}
