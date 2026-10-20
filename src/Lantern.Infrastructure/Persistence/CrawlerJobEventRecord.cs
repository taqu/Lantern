using Lantern.Core.Jobs;

namespace Lantern.Infrastructure.Persistence;

public sealed class CrawlerJobEventRecord
{
    public long Id { get; set; }
    public Guid JobId { get; set; }
    public string EventType { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public string? Details { get; set; }

    public CrawlerJobEvent ToDomain() => new()
    {
        Id = Id,
        JobId = JobId,
        EventType = Enum.Parse<JobEventType>(EventType),
        OccurredAt = OccurredAt,
        Details = Details
    };

    public static CrawlerJobEventRecord FromDomain(CrawlerJobEvent e) => new()
    {
        Id = e.Id,
        JobId = e.JobId,
        EventType = e.EventType.ToString(),
        OccurredAt = e.OccurredAt,
        Details = e.Details
    };
}
