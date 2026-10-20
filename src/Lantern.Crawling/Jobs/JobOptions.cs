using Microsoft.Extensions.Configuration;

namespace Lantern.Crawling.Jobs;

public sealed class JobOptions
{
    public const string SectionName = "CrawlerJobs";

    public int MaximumConcurrentJobs { get; init; } = 2;
    public int QueueCapacity { get; init; } = 32;
    public int DispatcherPollIntervalSeconds { get; init; } = 2;
    public int ProgressPersistenceIntervalSeconds { get; init; } = 5;
    public int HistoryRetentionDays { get; init; } = 90;
    public int EventRetentionDays { get; init; } = 30;
}
