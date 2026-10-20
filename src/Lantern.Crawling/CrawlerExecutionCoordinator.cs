using Lantern.Core.Crawling;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Lantern.Crawling;

public sealed record CrawlerExecutionRecord(
    Guid ExecutionId,
    Guid CrawlerId,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    CrawlStatus? Status,
    FileCrawlResult? Result,
    bool IsRunning);

// Single-instance in-memory coordinator. Does not persist execution history across restarts.
// Cross-process coordination is not implemented; Lantern targets a single application instance.
public sealed class CrawlerExecutionCoordinator : IDisposable
{
    private readonly struct ActiveEntry(Guid executionId, DateTimeOffset startedAt, CancellationTokenSource cts)
    {
        public Guid ExecutionId { get; } = executionId;
        public DateTimeOffset StartedAt { get; } = startedAt;
        public CancellationTokenSource Cts { get; } = cts;
    }

    private readonly IFileCrawler _crawler;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<CrawlerExecutionCoordinator> _logger;
    private readonly ConcurrentDictionary<Guid, ActiveEntry> _active = new();
    private readonly ConcurrentDictionary<Guid, CrawlerExecutionRecord> _lastRun = new();

    public CrawlerExecutionCoordinator(
        IFileCrawler crawler,
        IHostApplicationLifetime lifetime,
        ILogger<CrawlerExecutionCoordinator> logger)
    {
        _crawler = crawler;
        _lifetime = lifetime;
        _logger = logger;
    }

    // Returns false if the crawler is already running.
    public bool TryStart(Guid crawlerId, FileCrawlerConfiguration config)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ApplicationStopping);
        var executionId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;
        var entry = new ActiveEntry(executionId, startedAt, cts);

        if (!_active.TryAdd(crawlerId, entry))
        {
            cts.Dispose();
            return false;
        }

        _ = RunAsync(crawlerId, entry, config);
        return true;
    }

    public bool IsRunning(Guid crawlerId) => _active.ContainsKey(crawlerId);

    public CrawlerExecutionRecord? GetStatus(Guid crawlerId)
    {
        if (_active.TryGetValue(crawlerId, out ActiveEntry entry))
            return new CrawlerExecutionRecord(entry.ExecutionId, crawlerId, entry.StartedAt, null, null, null, IsRunning: true);
        _lastRun.TryGetValue(crawlerId, out CrawlerExecutionRecord? record);
        return record;
    }

    private async Task RunAsync(Guid crawlerId, ActiveEntry entry, FileCrawlerConfiguration config)
    {
        try
        {
            FileCrawlResult result = await _crawler.CrawlAsync(config, entry.Cts.Token);
            _lastRun[crawlerId] = new CrawlerExecutionRecord(
                entry.ExecutionId, crawlerId, entry.StartedAt, result.FinishedAt,
                result.Status, result, IsRunning: false);
            _logger.LogInformation("Crawl {ExecutionId} for {CrawlerId} completed with status {Status}",
                entry.ExecutionId, crawlerId, result.Status);
        }
        catch (OperationCanceledException)
        {
            _lastRun[crawlerId] = new CrawlerExecutionRecord(
                entry.ExecutionId, crawlerId, entry.StartedAt, DateTimeOffset.UtcNow,
                CrawlStatus.Canceled, null, IsRunning: false);
            _logger.LogInformation("Crawl {ExecutionId} for {CrawlerId} was canceled", entry.ExecutionId, crawlerId);
        }
        catch (Exception ex)
        {
            _lastRun[crawlerId] = new CrawlerExecutionRecord(
                entry.ExecutionId, crawlerId, entry.StartedAt, DateTimeOffset.UtcNow,
                CrawlStatus.Failed, null, IsRunning: false);
            _logger.LogError(ex, "Crawl {ExecutionId} for {CrawlerId} failed", entry.ExecutionId, crawlerId);
        }
        finally
        {
            _active.TryRemove(crawlerId, out _);
            entry.Cts.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (ActiveEntry entry in _active.Values)
        {
            try { entry.Cts.Cancel(); entry.Cts.Dispose(); }
            catch { /* best-effort on shutdown */ }
        }
        _active.Clear();
    }
}
