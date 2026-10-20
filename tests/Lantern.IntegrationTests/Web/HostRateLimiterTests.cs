using Lantern.Crawling.Web;

namespace Lantern.IntegrationTests.Web;

public class HostRateLimiterTests
{
    [Fact]
    public async Task WaitAsync_TwoConcurrentCallsSameHost_BothComplete()
    {
        using var limiter = new HostRateLimiter(delayMs: 0, maxConcurrentPerHost: 2);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        Task t1 = limiter.WaitAsync("https://example.com/page1", cts.Token);
        Task t2 = limiter.WaitAsync("https://example.com/page2", cts.Token);

        await Task.WhenAll(t1, t2);
        // No exception = pass
    }

    [Fact]
    public async Task WaitAsync_TwoSequentialCallsSameHost_BothComplete()
    {
        using var limiter = new HostRateLimiter(delayMs: 0, maxConcurrentPerHost: 1);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await limiter.WaitAsync("https://example.com/page1", cts.Token);
        await limiter.WaitAsync("https://example.com/page2", cts.Token);
        // No exception = pass
    }

    [Fact]
    public async Task WaitAsync_CancellationBeforeDelay_ThrowsOperationCanceledException()
    {
        using var limiter = new HostRateLimiter(delayMs: 10_000, maxConcurrentPerHost: 1);

        // First call "occupies" the slot and sets NextAllowedAt 10 seconds ahead.
        // We need the first call to run, then cancel on the second.
        // Actually with maxConcurrent=1, both calls will queue; the delay kicks in after acquiring the semaphore.
        // Use a fresh limiter so first call sets the NextAllowedAt, then we cancel the second call.
        using var limiter2 = new HostRateLimiter(delayMs: 10_000, maxConcurrentPerHost: 1);

        // Seed the limiter by running one call
        using var seedCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await limiter2.WaitAsync("https://example.com/seed", seedCts.Token);

        // Now the next call will wait delayMs (10s) before proceeding — cancel it quickly
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => limiter2.WaitAsync("https://example.com/page", cts.Token));
    }

    [Fact]
    public async Task WaitAsync_DifferentHosts_DoNotShareSlot()
    {
        // With maxConcurrentPerHost=1 and delayMs=0, two different hosts
        // should both be able to proceed concurrently without blocking each other.
        using var limiter = new HostRateLimiter(delayMs: 0, maxConcurrentPerHost: 1);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        Task t1 = limiter.WaitAsync("https://example.com/page", cts.Token);
        Task t2 = limiter.WaitAsync("https://other.com/page", cts.Token);

        await Task.WhenAll(t1, t2);
        // No hang/exception = different host slots are independent
    }

    [Fact]
    public void Dispose_ReleasesResourcesWithoutThrowing()
    {
        var limiter = new HostRateLimiter(delayMs: 0, maxConcurrentPerHost: 2);
        // Should not throw
        limiter.Dispose();
    }

    [Fact]
    public void Dispose_CanBeCalledTwice_NoException()
    {
        var limiter = new HostRateLimiter(delayMs: 0, maxConcurrentPerHost: 1);
        limiter.Dispose();
        // SemaphoreSlim.Dispose is idempotent; our Dispose clears the dict so second call is fine
        var ex = Record.Exception(() => limiter.Dispose());
        Assert.Null(ex);
    }
}
