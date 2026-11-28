using Lantern.Core.Jobs;
using Lantern.Crawling.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lantern.IntegrationTests.Jobs;

/// <summary>
/// Unit-style tests for CrawlerSchedulerService error classification, retry behaviour, and health state.
/// Uses a fake ICrawlerScheduleRepository so no SQLite database is needed.
/// CrawlerJobService and JobDispatcherService are never reached when GetDueSchedulesAsync returns empty or throws.
/// </summary>
public sealed class CrawlerSchedulerServiceTests
{
    // BackoffBaseSeconds = 0 → all backoff delays are 0ms, so retry tests complete instantly.
    private static IOptions<JobOptions> FastOptions() =>
        Options.Create(new JobOptions { DispatcherPollIntervalSeconds = 0, BackoffBaseSeconds = 0 });

    private static CrawlerSchedulerService BuildService(ICrawlerScheduleRepository repo) =>
        new(repo, jobs: null!, jobService: null!, dispatcher: null!, FastOptions(),
            NullLogger<CrawlerSchedulerService>.Instance);

    private static async Task WaitForFinalStateAsync(CrawlerSchedulerService svc, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while(svc.GetStatus().State is SchedulerState.Starting or SchedulerState.Running
               && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(2);
    }

    // ── Cancellation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CancellationTransitionsToStopped()
    {
        FakeScheduleRepository repo = new FakeScheduleRepository(_ => []);
        CrawlerSchedulerService svc = BuildService(repo);

        using CancellationTokenSource cts = new CancellationTokenSource();
        await svc.StartAsync(cts.Token);

        // Let the service run at least one cycle (transitions to Running).
        await WaitForStateAsync(svc, s => s == SchedulerState.Running, TimeSpan.FromSeconds(2));
        await svc.StopAsync(CancellationToken.None);

        Assert.Equal(SchedulerState.Stopped, svc.GetStatus().State);
    }

    // ── Permanent failure ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_PermanentErrorTransitionsToFaultedImmediately()
    {
        FakeScheduleRepository repo = new FakeScheduleRepository(_ => throw new InvalidOperationException(
            "The LINQ expression could not be translated."));
        CrawlerSchedulerService svc = BuildService(repo);

        using CancellationTokenSource cts = new CancellationTokenSource();
        await svc.StartAsync(cts.Token);
        await WaitForFinalStateAsync(svc, TimeSpan.FromSeconds(5));
        await svc.StopAsync(CancellationToken.None);

        Assert.Equal(SchedulerState.Faulted, svc.GetStatus().State);
    }

    [Fact]
    public async Task ExecuteAsync_PermanentErrorCallsRepositoryExactlyOnce()
    {
        int callCount = 0;
        FakeScheduleRepository repo = new FakeScheduleRepository(_ => {
            Interlocked.Increment(ref callCount);
            throw new InvalidOperationException("could not be translated");
        });
        CrawlerSchedulerService svc = BuildService(repo);

        using CancellationTokenSource cts = new CancellationTokenSource();
        await svc.StartAsync(cts.Token);
        await WaitForFinalStateAsync(svc, TimeSpan.FromSeconds(5));
        await svc.StopAsync(CancellationToken.None);

        Assert.Equal(1, callCount);
    }

    // ── Transient / unknown failure ───────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_UnknownErrorExhaustsRetryAndFaults()
    {
        int callCount = 0;
        FakeScheduleRepository repo = new FakeScheduleRepository(_ => {
            Interlocked.Increment(ref callCount);
            throw new Exception("transient failure");
        });
        CrawlerSchedulerService svc = BuildService(repo);

        using CancellationTokenSource cts = new CancellationTokenSource();
        await svc.StartAsync(cts.Token);
        await WaitForFinalStateAsync(svc, TimeSpan.FromSeconds(5));
        await svc.StopAsync(CancellationToken.None);

        Assert.Equal(SchedulerState.Faulted, svc.GetStatus().State);
        Assert.Equal(5, callCount); // MaxConsecutiveFailures = 5
    }

    [Fact]
    public async Task ExecuteAsync_SuccessAfterTransientResetsFailureCounter()
    {
        int callCount = 0;
        // Fail 4 times (below limit), then succeed.
        FakeScheduleRepository repo = new FakeScheduleRepository(_ => {
            int n = Interlocked.Increment(ref callCount);
            if(n <= 4) throw new Exception("transient");
            return [];
        });
        CrawlerSchedulerService svc = BuildService(repo);

        using CancellationTokenSource cts = new CancellationTokenSource();
        await svc.StartAsync(cts.Token);

        // Wait until at least 5 calls have happened (4 failures + 1 success).
        await WaitForCallCountAsync(() => callCount, 5, TimeSpan.FromSeconds(5));
        await svc.StopAsync(CancellationToken.None);

        // Service should be Stopped (not Faulted) because a success reset the counter.
        Assert.Equal(SchedulerState.Stopped, svc.GetStatus().State);
    }

    // ── Scheduler state ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatus_ReturnsRunningWhileSchedulerIsActive()
    {
        FakeScheduleRepository repo = new FakeScheduleRepository(_ => []);
        CrawlerSchedulerService svc = BuildService(repo);

        using CancellationTokenSource cts = new CancellationTokenSource();
        await svc.StartAsync(cts.Token);

        await WaitForStateAsync(svc, s => s == SchedulerState.Running, TimeSpan.FromSeconds(2));
        Assert.Equal(SchedulerState.Running, svc.GetStatus().State);

        await svc.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task GetStatus_FaultedStatusPreservesErrorMessage()
    {
        const string errorMsg = "The LINQ expression could not be translated.";
        FakeScheduleRepository repo = new FakeScheduleRepository(_ => throw new InvalidOperationException(errorMsg));
        CrawlerSchedulerService svc = BuildService(repo);

        using CancellationTokenSource cts = new CancellationTokenSource();
        await svc.StartAsync(cts.Token);
        await WaitForFinalStateAsync(svc, TimeSpan.FromSeconds(5));
        await svc.StopAsync(CancellationToken.None);

        Assert.Equal(errorMsg, svc.GetStatus().LastError);
        Assert.NotNull(svc.GetStatus().FaultedAt);
    }

    // ── Cancellation token isolation ──────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_UnexpectedOperationCanceledIsTreatedAsTransient()
    {
        // An OperationCanceledException that is NOT from stoppingToken (e.g. an internal
        // per-operation timeout) must be counted as a transient failure, not silently swallowed.
        int callCount = 0;
        FakeScheduleRepository repo = new FakeScheduleRepository(_ => {
            Interlocked.Increment(ref callCount);
            throw new OperationCanceledException("operation timed out");
        });
        CrawlerSchedulerService svc = BuildService(repo);

        using CancellationTokenSource cts = new CancellationTokenSource();
        await svc.StartAsync(cts.Token);
        await WaitForFinalStateAsync(svc, TimeSpan.FromSeconds(5));
        await svc.StopAsync(CancellationToken.None);

        Assert.Equal(SchedulerState.Faulted, svc.GetStatus().State);
        Assert.Equal(5, callCount); // MaxConsecutiveFailures = 5
    }

    [Fact]
    public async Task ExecuteAsync_CancellationDuringQuery_TransitionsToStopped()
    {
        // When stoppingToken is canceled while a repository query is in progress,
        // the OperationCanceledException must propagate as expected shutdown (not error).
        FakeScheduleRepositoryAsync repo = new FakeScheduleRepositoryAsync(async (_, ct) => {
            await Task.Delay(Timeout.Infinite, ct);
            return [];
        });
        CrawlerSchedulerService svc = BuildService(repo);

        await svc.StartAsync(CancellationToken.None);
        await WaitForStateAsync(svc, s => s != SchedulerState.Starting, TimeSpan.FromSeconds(2));
        await svc.StopAsync(CancellationToken.None);

        Assert.Equal(SchedulerState.Stopped, svc.GetStatus().State);
    }

    [Fact]
    public async Task ExecuteAsync_RepeatedPolling_DoesNotFault()
    {
        // The scheduler must run multiple poll cycles without accumulating failure counts
        // or producing unexpected cancellation.
        int callCount = 0;
        FakeScheduleRepository repo = new FakeScheduleRepository(_ => {
            Interlocked.Increment(ref callCount);
            return [];
        });
        CrawlerSchedulerService svc = BuildService(repo);

        await svc.StartAsync(CancellationToken.None);
        await WaitForCallCountAsync(() => callCount, 3, TimeSpan.FromSeconds(5));
        await svc.StopAsync(CancellationToken.None);

        Assert.Equal(SchedulerState.Stopped, svc.GetStatus().State);
        Assert.True(callCount >= 3, $"Expected at least 3 poll cycles, got {callCount}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task WaitForStateAsync(CrawlerSchedulerService svc, Func<SchedulerState, bool> predicate, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while(!predicate(svc.GetStatus().State) && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(2);
    }

    private static async Task WaitForCallCountAsync(Func<int> getCount, int target, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while(getCount() < target && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(2);
    }

    // ── Fake repositories ─────────────────────────────────────────────────────

    private sealed class FakeScheduleRepository(
        Func<DateTimeOffset, IReadOnlyList<CrawlerSchedule>> getDue) : ICrawlerScheduleRepository
    {
        public Task<CrawlerSchedule?> GetScheduleAsync(Guid crawlerId, CancellationToken ct = default)
            => Task.FromResult<CrawlerSchedule?>(null);

        public Task SaveScheduleAsync(CrawlerSchedule schedule, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<CrawlerSchedule>> GetDueSchedulesAsync(DateTimeOffset now, CancellationToken ct = default)
            => Task.FromResult(getDue(now));

        public Task UpdateLastTriggeredAsync(Guid crawlerId, DateTimeOffset triggeredAt, DateTimeOffset nextRunAt, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteScheduleAsync(Guid crawlerId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeScheduleRepositoryAsync(
        Func<DateTimeOffset, CancellationToken, Task<IReadOnlyList<CrawlerSchedule>>> getDue) : ICrawlerScheduleRepository
    {
        public Task<CrawlerSchedule?> GetScheduleAsync(Guid crawlerId, CancellationToken ct = default)
            => Task.FromResult<CrawlerSchedule?>(null);

        public Task SaveScheduleAsync(CrawlerSchedule schedule, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<CrawlerSchedule>> GetDueSchedulesAsync(DateTimeOffset now, CancellationToken ct = default)
            => getDue(now, ct);

        public Task UpdateLastTriggeredAsync(Guid crawlerId, DateTimeOffset triggeredAt, DateTimeOffset nextRunAt, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteScheduleAsync(Guid crawlerId, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
