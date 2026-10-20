using Lantern.Core.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Lantern.Infrastructure.Persistence;

public sealed class CrawlerScheduleRepository(IDbContextFactory<LanternDbContext> factory) : ICrawlerScheduleRepository
{
    public async Task<CrawlerSchedule?> GetScheduleAsync(Guid crawlerId, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        CrawlerScheduleRecord? rec = await db.CrawlerSchedules.AsNoTracking()
            .FirstOrDefaultAsync(s => s.CrawlerId == crawlerId, ct);
        return rec?.ToDomain();
    }

    public async Task SaveScheduleAsync(CrawlerSchedule schedule, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        CrawlerScheduleRecord? existing = await db.CrawlerSchedules
            .FirstOrDefaultAsync(s => s.CrawlerId == schedule.CrawlerId, ct);
        if (existing == null)
        {
            db.CrawlerSchedules.Add(CrawlerScheduleRecord.FromDomain(schedule));
        }
        else
        {
            CrawlerScheduleRecord updated = CrawlerScheduleRecord.FromDomain(schedule);
            db.Entry(existing).CurrentValues.SetValues(updated);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CrawlerSchedule>> GetDueSchedulesAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        List<CrawlerScheduleRecord> recs = await db.CrawlerSchedules.AsNoTracking()
            .Where(s => s.Enabled && s.Type != "Disabled" && s.NextRunAt != null && s.NextRunAt <= now)
            .ToListAsync(ct);
        return recs.Select(r => r.ToDomain()).ToList();
    }

    public async Task UpdateLastTriggeredAsync(Guid crawlerId, DateTimeOffset triggeredAt, DateTimeOffset nextRunAt, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        await db.CrawlerSchedules
            .Where(s => s.CrawlerId == crawlerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.LastTriggeredAt, triggeredAt)
                .SetProperty(r => r.NextRunAt, nextRunAt),
                ct);
    }

    public async Task DeleteScheduleAsync(Guid crawlerId, CancellationToken ct = default)
    {
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        await db.CrawlerSchedules.Where(s => s.CrawlerId == crawlerId).ExecuteDeleteAsync(ct);
    }
}
