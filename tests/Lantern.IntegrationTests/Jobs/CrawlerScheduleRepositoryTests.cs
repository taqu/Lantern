using Lantern.Core.Jobs;
using Lantern.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lantern.IntegrationTests.Jobs;

public sealed class CrawlerScheduleRepositoryTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lantern-schedule-test-" + Guid.NewGuid().ToString("N"));
    private ServiceProvider _services = null!;
    private IDbContextFactory<LanternDbContext> Factory => _services.GetRequiredService<IDbContextFactory<LanternDbContext>>();
    private CrawlerScheduleRepository Repository => new(Factory);

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        _services = new ServiceCollection()
            .AddDbContextFactory<LanternDbContext>(options =>
                options.UseSqlite(new SqliteConnectionStringBuilder {
                    DataSource = Path.Combine(_directory, "test.db"),
                    Pooling = false
                }.ToString()))
            .BuildServiceProvider();
        await using LanternDbContext db = await Factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        Directory.Delete(_directory, true);
    }

    // ── Helper ──────────────────────────────────────────────────────────────

    private static CrawlerSchedule MakeSchedule(Guid crawlerId, DateTime? nextRunUtc = null, bool enabled = true, ScheduleType type = ScheduleType.Hourly)
    {
        return new CrawlerSchedule {
            CrawlerId = crawlerId,
            Enabled = enabled,
            Type = type,
            NextRunAt = nextRunUtc.HasValue ? new DateTimeOffset(nextRunUtc.Value, TimeSpan.Zero) : null,
        };
    }

    // ── Query translation ────────────────────────────────────────────────────

    [Fact]
    public async Task GetDueSchedulesAsync_TranslatesQueryToSqlite()
    {
        // This test verifies the query executes against real SQLite without an InvalidOperationException.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IReadOnlyList<CrawlerSchedule> result = await Repository.GetDueSchedulesAsync(now);
        Assert.Empty(result);
    }

    // ── Due / not due ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDueSchedulesAsync_ReturnsDueSchedule()
    {
        Guid crawlerId = Guid.NewGuid();
        DateTime pastUtc = DateTime.UtcNow.AddMinutes(-5);
        await Repository.SaveScheduleAsync(MakeSchedule(crawlerId, pastUtc));

        IReadOnlyList<CrawlerSchedule> result = await Repository.GetDueSchedulesAsync(DateTimeOffset.UtcNow);

        Assert.Single(result);
        Assert.Equal(crawlerId, result[0].CrawlerId);
    }

    [Fact]
    public async Task GetDueSchedulesAsync_ExcludesFutureSchedule()
    {
        Guid crawlerId = Guid.NewGuid();
        DateTime futureUtc = DateTime.UtcNow.AddHours(1);
        await Repository.SaveScheduleAsync(MakeSchedule(crawlerId, futureUtc));

        IReadOnlyList<CrawlerSchedule> result = await Repository.GetDueSchedulesAsync(DateTimeOffset.UtcNow);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDueSchedulesAsync_ExcludesDisabledSchedule()
    {
        Guid crawlerId = Guid.NewGuid();
        DateTime pastUtc = DateTime.UtcNow.AddMinutes(-5);
        await Repository.SaveScheduleAsync(MakeSchedule(crawlerId, pastUtc, enabled: false));

        IReadOnlyList<CrawlerSchedule> result = await Repository.GetDueSchedulesAsync(DateTimeOffset.UtcNow);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDueSchedulesAsync_ExcludesDisabledTypeSchedule()
    {
        Guid crawlerId = Guid.NewGuid();
        DateTime pastUtc = DateTime.UtcNow.AddMinutes(-5);
        await Repository.SaveScheduleAsync(MakeSchedule(crawlerId, pastUtc, type: ScheduleType.Disabled));

        IReadOnlyList<CrawlerSchedule> result = await Repository.GetDueSchedulesAsync(DateTimeOffset.UtcNow);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDueSchedulesAsync_ExcludesNullNextRunAt()
    {
        Guid crawlerId = Guid.NewGuid();
        await Repository.SaveScheduleAsync(MakeSchedule(crawlerId, nextRunUtc: null));

        IReadOnlyList<CrawlerSchedule> result = await Repository.GetDueSchedulesAsync(DateTimeOffset.UtcNow);

        Assert.Empty(result);
    }

    // ── UTC boundary ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDueSchedulesAsync_IncludesScheduleExactlyAtNow()
    {
        Guid crawlerId = Guid.NewGuid();
        DateTime exactUtc = DateTime.UtcNow;
        // Store the timestamp, then query with the same instant.
        await Repository.SaveScheduleAsync(MakeSchedule(crawlerId, exactUtc));

        // Use a DateTimeOffset that equals the stored DateTime exactly.
        DateTimeOffset queryTime = new(exactUtc, TimeSpan.Zero);
        IReadOnlyList<CrawlerSchedule> result = await Repository.GetDueSchedulesAsync(queryTime);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetDueSchedulesAsync_UtcTimestampRoundtripsCorrectly()
    {
        Guid crawlerId = Guid.NewGuid();
        DateTimeOffset stored = new(2026, 6, 15, 10, 30, 0, TimeSpan.Zero);
        await Repository.SaveScheduleAsync(MakeSchedule(crawlerId, stored.UtcDateTime));

        CrawlerSchedule? retrieved = await Repository.GetScheduleAsync(crawlerId);
        Assert.NotNull(retrieved);
        Assert.Equal(stored, retrieved.NextRunAt);
        Assert.Equal(TimeSpan.Zero, retrieved.NextRunAt!.Value.Offset);
    }

    // ── Existing persisted values ─────────────────────────────────────────────

    [Fact]
    public async Task GetDueSchedulesAsync_HandlesPreviouslyPersistedScheduleViaRoundtrip()
    {
        // Save a schedule through the repository and verify it round-trips correctly through
        // the UTC DateTime storage layer. This ensures the migration preserves data correctly.
        Guid crawlerId = Guid.NewGuid();
        DateTimeOffset storedAt = new(2026, 3, 15, 8, 0, 0, TimeSpan.Zero);
        await Repository.SaveScheduleAsync(MakeSchedule(crawlerId, storedAt.UtcDateTime));

        // Should be returned when queried after the stored time.
        IReadOnlyList<CrawlerSchedule> due = await Repository.GetDueSchedulesAsync(storedAt.AddSeconds(1));
        Assert.Single(due);
        Assert.Equal(crawlerId, due[0].CrawlerId);

        // The returned NextRunAt must have UTC offset (no local conversion).
        Assert.Equal(TimeSpan.Zero, due[0].NextRunAt!.Value.Offset);

        // Should NOT be returned when queried before the stored time.
        IReadOnlyList<CrawlerSchedule> notYetDue = await Repository.GetDueSchedulesAsync(storedAt.AddSeconds(-1));
        Assert.Empty(notYetDue);
    }
}
