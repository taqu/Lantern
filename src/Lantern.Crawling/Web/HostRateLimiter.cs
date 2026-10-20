namespace Lantern.Crawling.Web;

/// <summary>
/// Per-host request throttling: enforces minimum delay and bounded concurrency per host.
/// </summary>
public sealed class HostRateLimiter(int delayMs, int maxConcurrentPerHost) : IDisposable
{
    private readonly Dictionary<string, HostSlot> _slots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    public async Task WaitAsync(string url, CancellationToken ct)
    {
        string host = UrlNormalizer.GetHost(url) ?? url;
        HostSlot slot = GetOrCreate(host);
        await slot.Semaphore.WaitAsync(ct);
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset earliest = slot.NextAllowedAt;
            if (earliest > now)
            {
                int waitMs = (int)(earliest - now).TotalMilliseconds;
                await Task.Delay(waitMs, ct);
            }
            slot.NextAllowedAt = DateTimeOffset.UtcNow.AddMilliseconds(delayMs);
        }
        finally
        {
            slot.Semaphore.Release();
        }
    }

    private HostSlot GetOrCreate(string host)
    {
        lock (_lock)
        {
            if (!_slots.TryGetValue(host, out HostSlot? slot))
            {
                slot = new HostSlot(new SemaphoreSlim(maxConcurrentPerHost, maxConcurrentPerHost));
                _slots[host] = slot;
            }
            return slot;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (HostSlot slot in _slots.Values)
                slot.Semaphore.Dispose();
            _slots.Clear();
        }
    }

    private sealed class HostSlot(SemaphoreSlim semaphore)
    {
        public SemaphoreSlim Semaphore { get; } = semaphore;
        public DateTimeOffset NextAllowedAt { get; set; } = DateTimeOffset.MinValue;
    }
}
