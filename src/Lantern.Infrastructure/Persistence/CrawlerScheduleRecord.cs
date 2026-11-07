using Lantern.Core.Jobs;

namespace Lantern.Infrastructure.Persistence;

public sealed class CrawlerScheduleRecord
{
    public Guid CrawlerId { get; set; }
    public bool Enabled { get; set; }
    public string Type { get; set; } = "Disabled";
    public string? TimeOfDay { get; set; }
    public string? TimeZoneId { get; set; }
    public string? DaysOfWeek { get; set; }
    public DateTime? NextRunAt { get; set; }
    public DateTime? LastTriggeredAt { get; set; }

    public CrawlerSchedule ToDomain() => new()
    {
        CrawlerId = CrawlerId,
        Enabled = Enabled,
        Type = Enum.Parse<ScheduleType>(Type),
        TimeOfDay = TimeOfDay != null ? TimeOnly.Parse(TimeOfDay) : null,
        TimeZoneId = TimeZoneId,
        DaysOfWeek = ParseDaysOfWeek(DaysOfWeek),
        NextRunAt = NextRunAt.HasValue ? new DateTimeOffset(NextRunAt.Value, TimeSpan.Zero) : null,
        LastTriggeredAt = LastTriggeredAt.HasValue ? new DateTimeOffset(LastTriggeredAt.Value, TimeSpan.Zero) : null
    };

    public static CrawlerScheduleRecord FromDomain(CrawlerSchedule s) => new()
    {
        CrawlerId = s.CrawlerId,
        Enabled = s.Enabled,
        Type = s.Type.ToString(),
        TimeOfDay = s.TimeOfDay?.ToString("HH:mm"),
        TimeZoneId = s.TimeZoneId,
        DaysOfWeek = s.DaysOfWeek.Count > 0
            ? string.Join(",", s.DaysOfWeek.Select(d => (int)d))
            : null,
        NextRunAt = s.NextRunAt?.UtcDateTime,
        LastTriggeredAt = s.LastTriggeredAt?.UtcDateTime
    };

    private static IReadOnlyList<DayOfWeek> ParseDaysOfWeek(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => (DayOfWeek)int.Parse(s))
            .ToList();
    }
}
