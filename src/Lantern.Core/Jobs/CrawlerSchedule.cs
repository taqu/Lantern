namespace Lantern.Core.Jobs;

public sealed class CrawlerSchedule
{
    public Guid CrawlerId { get; set; }
    public bool Enabled { get; set; }
    public ScheduleType Type { get; set; } = ScheduleType.Disabled;
    public TimeOnly? TimeOfDay { get; set; }
    public string? TimeZoneId { get; set; }
    public IReadOnlyList<DayOfWeek> DaysOfWeek { get; set; } = [];
    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset? LastTriggeredAt { get; set; }
}
