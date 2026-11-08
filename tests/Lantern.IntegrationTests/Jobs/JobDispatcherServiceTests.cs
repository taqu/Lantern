using Lantern.Core.Jobs;
using Lantern.Crawling.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lantern.IntegrationTests.Jobs;

/// <summary>
/// Unit-style tests for JobDispatcherService polling and shutdown behaviour.
/// Uses a fake ICrawlerJobRepository that always returns no pending jobs.
/// Verifies that the SemaphoreSlim-based dispatcher does not generate
/// OperationCanceledException during normal polling intervals.
/// </summary>
public sealed class JobDispatcherServiceTests
{
    private static IOptions<JobOptions> FastOptions() =>
        Options.Create(new JobOptions { DispatcherPollIntervalSeconds = 0, MaximumConcurrentJobs = 1 });

    private static JobDispatcherService BuildDispatcher(ICrawlerJobRepository? repo = null) =>
        new(repo ?? new FakeJobRepository(),
            fileCrawler: null!,
            FastOptions(),
            NullLogger<JobDispatcherService>.Instance);

    // ── Startup / shutdown ────────────────────────────────────────────────────

    [Fact]
    public async Task Dispatcher_StartsAndStopsCleanly()
    {
        using JobDispatcherService dispatcher = BuildDispatcher();

        await dispatcher.StartAsync(CancellationToken.None);
        await Task.Delay(50); // allow at least one poll cycle
        await dispatcher.StopAsync(CancellationToken.None);
        // Reaching here means no unhandled exception from the dispatcher loop.
    }

    [Fact]
    public async Task Dispatcher_MultiplePollCycles_DoNotThrow()
    {
        int queryCount = 0;
        var repo = new FakeJobRepository(onGetPending: () => Interlocked.Increment(ref queryCount));
        using JobDispatcherService dispatcher = BuildDispatcher(repo);

        await dispatcher.StartAsync(CancellationToken.None);

        // Wait for several poll cycles to complete.
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (queryCount < 3 && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(5);

        await dispatcher.StopAsync(CancellationToken.None);

        Assert.True(queryCount >= 3, $"Expected ≥3 poll cycles, got {queryCount}");
    }

    // ── Wake-up signal ────────────────────────────────────────────────────────

    [Fact]
    public async Task SignalNewJob_CalledRepeatedly_DoesNotThrow()
    {
        using JobDispatcherService dispatcher = BuildDispatcher();

        await dispatcher.StartAsync(CancellationToken.None);

        // Sending many signals must not throw and must not overflow internal state.
        for (int i = 0; i < 20; i++)
            dispatcher.SignalNewJob();

        await dispatcher.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SignalCancellation_ForUnknownJobId_DoesNotThrow()
    {
        using JobDispatcherService dispatcher = BuildDispatcher();

        await dispatcher.StartAsync(CancellationToken.None);
        dispatcher.SignalCancellation(Guid.NewGuid()); // unknown job — should be a no-op
        await dispatcher.StopAsync(CancellationToken.None);
    }

    // ── Fake repository ───────────────────────────────────────────────────────

    private sealed class FakeJobRepository(Action? onGetPending = null) : ICrawlerJobRepository
    {
        public Task<Guid> CreateJobAsync(CrawlerJob job, CancellationToken ct = default)
            => Task.FromResult(job.Id);

        public Task<CrawlerJob?> GetJobAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<CrawlerJob?>(null);

        public Task<bool> ClaimJobAsync(Guid jobId, DateTimeOffset startedAt, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> RequestCancellationAsync(Guid jobId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task UpdateProgressAsync(Guid jobId, JobProgress progress, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task CompleteJobAsync(Guid jobId, JobStatus status, JobProgress progress, string? errorMessage, DateTimeOffset finishedAt, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<CrawlerJob>> GetPendingJobsAsync(int maxCount, CancellationToken ct = default)
        {
            onGetPending?.Invoke();
            return Task.FromResult<IReadOnlyList<CrawlerJob>>([]);
        }

        public Task<bool> HasActiveJobForCrawlerAsync(Guid crawlerId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<CrawlerJob?> GetActiveJobForCrawlerAsync(Guid crawlerId, CancellationToken ct = default)
            => Task.FromResult<CrawlerJob?>(null);

        public Task<(IReadOnlyList<CrawlerJob> Items, int TotalCount)> GetJobsPagedAsync(
            int page, int pageSize, Guid? crawlerId = null, JobStatus? status = null, CancellationToken ct = default)
            => Task.FromResult<(IReadOnlyList<CrawlerJob>, int)>(([], 0));

        public Task MarkInterruptedJobsFailedAsync(string reason, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<int> DeleteOldJobsAsync(DateTimeOffset olderThan, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task AddEventAsync(CrawlerJobEvent evt, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<CrawlerJobEvent>> GetJobEventsAsync(Guid jobId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CrawlerJobEvent>>([]);

        public Task<int> DeleteOldEventsAsync(DateTimeOffset olderThan, CancellationToken ct = default)
            => Task.FromResult(0);
    }
}
