namespace Lantern.Core.Jobs;

public sealed class CrawlerJobEvent
{
    public long Id { get; set; }
    public Guid JobId { get; set; }
    public JobEventType EventType { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? Details { get; set; }
}
