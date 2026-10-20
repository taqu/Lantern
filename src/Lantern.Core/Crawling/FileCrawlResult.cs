namespace Lantern.Core.Crawling;

public sealed class FileCrawlResult
{
    public Guid CrawlerId { get; init; }
    public Guid ScanId { get; init; }
    public CrawlStatus Status { get; init; }
    public int DiscoveredFiles { get; init; }
    public int ProcessedFiles { get; init; }
    public int IndexedFiles { get; init; }
    public int UnchangedFiles { get; init; }
    public int SkippedFiles { get; init; }
    public int FailedFiles { get; init; }
    public int DeletedFiles { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset FinishedAt { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
}
