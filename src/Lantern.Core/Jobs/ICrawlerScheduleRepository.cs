namespace Lantern.Core.Jobs;

public interface ICrawlerScheduleRepository
{
    Task<CrawlerSchedule?> GetScheduleAsync(Guid crawlerId, CancellationToken ct = default);
    Task SaveScheduleAsync(CrawlerSchedule schedule, CancellationToken ct = default);
    Task<IReadOnlyList<CrawlerSchedule>> GetDueSchedulesAsync(DateTimeOffset now, CancellationToken ct = default);
    Task UpdateLastTriggeredAsync(Guid crawlerId, DateTimeOffset triggeredAt, DateTimeOffset nextRunAt, CancellationToken ct = default);
    Task DeleteScheduleAsync(Guid crawlerId, CancellationToken ct = default);
}
