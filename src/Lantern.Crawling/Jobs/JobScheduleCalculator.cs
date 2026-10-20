using Lantern.Core.Jobs;

namespace Lantern.Crawling.Jobs;

// Calculates the next UTC run time for a CrawlerSchedule.
// Missed occurrences during downtime are coalesced: at most one catch-up job is created.
public static class JobScheduleCalculator
{
    public static DateTimeOffset? CalculateNextRun(CrawlerSchedule schedule, DateTimeOffset after)
    {
        if (!schedule.Enabled || schedule.Type == ScheduleType.Disabled)
            return null;

        TimeZoneInfo tz = ResolveTimeZone(schedule.TimeZoneId);

        return schedule.Type switch
        {
            ScheduleType.Hourly => after.AddHours(1),
            ScheduleType.Daily => NextDaily(schedule.TimeOfDay ?? new TimeOnly(2, 0), tz, after),
            ScheduleType.Weekly => NextWeekly(schedule.TimeOfDay ?? new TimeOnly(2, 0), schedule.DaysOfWeek, tz, after),
            _ => null
        };
    }

    // Called at startup or when a schedule is saved: returns a due time if missed, else next future occurrence.
    public static DateTimeOffset? CalculateInitialNextRun(CrawlerSchedule schedule, DateTimeOffset now)
    {
        if (!schedule.Enabled || schedule.Type == ScheduleType.Disabled)
            return null;

        DateTimeOffset? next = CalculateNextRun(schedule, now.AddSeconds(-1));
        if (next == null) return null;
        // If overdue (next was in the past), schedule immediately for a catch-up run
        return next <= now ? now : next;
    }

    private static DateTimeOffset NextDaily(TimeOnly timeOfDay, TimeZoneInfo tz, DateTimeOffset after)
    {
        DateTimeOffset localAfter = TimeZoneInfo.ConvertTime(after, tz);
        DateTime candidateDt = localAfter.Date + timeOfDay.ToTimeSpan();
        TimeSpan offset = tz.GetUtcOffset(candidateDt);
        var candidate = new DateTimeOffset(candidateDt, offset);
        if (candidate <= after) candidate = candidate.AddDays(1);
        return TimeZoneInfo.ConvertTimeToUtc(candidate.DateTime, tz);
    }

    private static DateTimeOffset NextWeekly(TimeOnly timeOfDay, IReadOnlyList<DayOfWeek> days, TimeZoneInfo tz, DateTimeOffset after)
    {
        if (days.Count == 0) return after.AddDays(7);
        DateTimeOffset localAfter = TimeZoneInfo.ConvertTime(after, tz);

        for (int i = 0; i <= 7; i++)
        {
            DateTime candidateDt = localAfter.Date.AddDays(i) + timeOfDay.ToTimeSpan();
            if (i == 0 && candidateDt <= localAfter.DateTime) continue;
            if (!days.Contains(candidateDt.DayOfWeek)) continue;
            TimeSpan offset = tz.GetUtcOffset(candidateDt);
            var candidate = new DateTimeOffset(candidateDt, offset);
            if (candidate > after)
                return TimeZoneInfo.ConvertTimeToUtc(candidate.DateTime, tz);
        }

        // Fallback: scan next 14 days
        for (int i = 1; i <= 14; i++)
        {
            DateTime candidateDt = localAfter.Date.AddDays(i) + timeOfDay.ToTimeSpan();
            if (!days.Contains(candidateDt.DayOfWeek)) continue;
            TimeSpan offset = tz.GetUtcOffset(candidateDt);
            var candidate = new DateTimeOffset(candidateDt, offset);
            if (candidate > after)
                return TimeZoneInfo.ConvertTimeToUtc(candidate.DateTime, tz);
        }

        return after.AddDays(7);
    }

    private static TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { return TimeZoneInfo.Utc; }
    }
}
