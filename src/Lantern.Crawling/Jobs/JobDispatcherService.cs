using Lantern.Core.Crawling;
using Lantern.Core.Jobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

namespace Lantern.Crawling.Jobs;

/// <summary>
/// Background service that claims pending jobs and executes them via the file crawler.
/// SQLite is the authoritative queue; the in-memory channel is a wake-up signal only.
/// Per-crawler exclusion is enforced by the atomic SQL claim in ICrawlerJobRepository.
/// </summary>
public sealed class JobDispatcherService : BackgroundService
{
    private readonly ICrawlerJobRepository _jobs;
    private readonly IFileCrawler _fileCrawler;
    private readonly IWebCrawler? _webCrawler;
    private readonly IOptions<JobOptions> _options;
    private readonly ILogger<JobDispatcherService> _logger;
    private readonly Channel<bool> _wakeUp;
    private SemaphoreSlim _concurrency = null!;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _activeCts = new();
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public JobDispatcherService(
        ICrawlerJobRepository jobs,
        IFileCrawler fileCrawler,
        IOptions<JobOptions> options,
        ILogger<JobDispatcherService> logger,
        IWebCrawler? webCrawler = null)
    {
        _jobs = jobs;
        _fileCrawler = fileCrawler;
        _webCrawler = webCrawler;
        _options = options;
        _logger = logger;
        _wakeUp = Channel.CreateBounded<bool>(new BoundedChannelOptions(4)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true
        });
    }

    // Called by CrawlerJobService after a job is persisted to signal a dispatch attempt.
    public void SignalNewJob() => _wakeUp.Writer.TryWrite(true);

    // Called by the cancellation path to cancel an in-flight job CTS.
    public void SignalCancellation(Guid jobId)
    {
        if (_activeCts.TryGetValue(jobId, out CancellationTokenSource? cts))
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _concurrency = new SemaphoreSlim(_options.Value.MaximumConcurrentJobs, _options.Value.MaximumConcurrentJobs);

        // Recovery: mark any orphaned Running jobs as Failed
        await RecoverAsync(stoppingToken);

        _logger.LogInformation("Job dispatcher started (maxConcurrent={Max})", _options.Value.MaximumConcurrentJobs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await DispatchPendingAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Dispatcher loop error"); }

            // Wait for wake-up signal or poll interval
            using CancellationTokenSource delayCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            delayCts.CancelAfter(TimeSpan.FromSeconds(_options.Value.DispatcherPollIntervalSeconds));
            try { await _wakeUp.Reader.ReadAsync(delayCts.Token); }
            catch (OperationCanceledException) { }
        }

        _logger.LogInformation("Job dispatcher stopping; waiting for active jobs to finish");
    }

    private async Task RecoverAsync(CancellationToken ct)
    {
        const string reason = "Application was interrupted while this job was running.";
        try
        {
            await _jobs.MarkInterruptedJobsFailedAsync(reason, ct);
            _logger.LogInformation("Startup recovery complete");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Startup recovery failed");
        }
    }

    private async Task DispatchPendingAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!await _concurrency.WaitAsync(0)) break;

            CrawlerJob? claimed = await TryClaimNextAsync(stoppingToken);
            if (claimed == null)
            {
                _concurrency.Release();
                break;
            }

            // Fire-and-forget; semaphore released in ExecuteJobAsync finally block
            _ = ExecuteJobAsync(claimed, stoppingToken);
        }
    }

    private async Task<CrawlerJob?> TryClaimNextAsync(CancellationToken ct)
    {
        IReadOnlyList<CrawlerJob> pending = await _jobs.GetPendingJobsAsync(8, ct);
        foreach (CrawlerJob job in pending)
        {
            if (await _jobs.ClaimJobAsync(job.Id, DateTimeOffset.UtcNow, ct))
            {
                await _jobs.AddEventAsync(new CrawlerJobEvent { JobId = job.Id, EventType = JobEventType.JobStarted, OccurredAt = DateTimeOffset.UtcNow }, ct);
                return job;
            }
        }
        return null;
    }

    private async Task ExecuteJobAsync(CrawlerJob job, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Executing job {JobId} for crawler {CrawlerId}", job.Id, job.CrawlerId);

        using CancellationTokenSource jobCts = new();
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(jobCts.Token, stoppingToken);
        _activeCts[job.Id] = jobCts;

        try
        {
            FileCrawlResult result;
            string crawlerType = DetectCrawlerType(job.ConfigurationSnapshot);

            if (crawlerType == "web")
            {
                if (_webCrawler == null)
                {
                    await FailJobAsync(job.Id, "Web crawler is not registered.", linkedCts.Token);
                    return;
                }
                WebCrawlerConfiguration? webConfig = ParseWebSnapshot(job.ConfigurationSnapshot);
                if (webConfig == null)
                {
                    await FailJobAsync(job.Id, "Invalid or missing web crawler configuration snapshot", linkedCts.Token);
                    return;
                }
                result = await _webCrawler.CrawlAsync(webConfig, linkedCts.Token);
            }
            else
            {
                FileCrawlerConfiguration? config = CrawlerJobService.ParseSnapshot(job.ConfigurationSnapshot);
                if (config == null)
                {
                    await FailJobAsync(job.Id, "Invalid or missing configuration snapshot", linkedCts.Token);
                    return;
                }
                result = await _fileCrawler.CrawlAsync(config, linkedCts.Token);
            }

            JobStatus status = result.Status switch
            {
                CrawlStatus.Completed => JobStatus.Completed,
                CrawlStatus.CompletedWithErrors => JobStatus.Completed,
                CrawlStatus.Failed => JobStatus.Failed,
                CrawlStatus.Canceled => JobStatus.Canceled,
                _ => JobStatus.Failed
            };

            string? errorMessage = result.Errors.Count > 0
                ? string.Join("; ", result.Errors.Take(5))
                : null;
            if (result.Status == CrawlStatus.CompletedWithErrors && errorMessage != null)
                errorMessage = $"Completed with errors: {errorMessage}";

            JobProgress progress = new()
            {
                DiscoveredFiles = result.DiscoveredFiles,
                ProcessedFiles = result.ProcessedFiles,
                IndexedFiles = result.IndexedFiles,
                UnchangedFiles = result.UnchangedFiles,
                SkippedFiles = result.SkippedFiles,
                FailedFiles = result.FailedFiles,
                DeletedFiles = result.DeletedFiles
            };

            await _jobs.CompleteJobAsync(job.Id, status, progress, errorMessage, result.FinishedAt, stoppingToken);
            await _jobs.AddEventAsync(new CrawlerJobEvent
            {
                JobId = job.Id,
                EventType = status == JobStatus.Completed ? JobEventType.JobCompleted
                    : status == JobStatus.Canceled ? JobEventType.JobCanceled
                    : JobEventType.JobFailed,
                OccurredAt = result.FinishedAt,
                Details = errorMessage
            }, stoppingToken);

            _logger.LogInformation("Job {JobId} finished with status {Status}", job.Id, status);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // Job-level cancellation (not app shutdown)
            await FailJobAsync(job.Id, "Canceled", stoppingToken, JobStatus.Canceled, JobEventType.JobCanceled);
        }
        catch (OperationCanceledException)
        {
            // App shutdown; mark as failed so it's recoverable on restart
            await FailJobAsync(job.Id, "Application shutting down", CancellationToken.None, JobStatus.Failed, JobEventType.JobInterrupted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in job {JobId}", job.Id);
            await FailJobAsync(job.Id, $"Unhandled exception: {ex.Message}", stoppingToken);
        }
        finally
        {
            _activeCts.TryRemove(job.Id, out _);
            _concurrency.Release();
            _wakeUp.Writer.TryWrite(true);
        }
    }

    private async Task FailJobAsync(Guid jobId, string reason, CancellationToken ct,
        JobStatus status = JobStatus.Failed, JobEventType eventType = JobEventType.JobFailed)
    {
        try
        {
            await _jobs.CompleteJobAsync(jobId, status, JobProgress.Empty, reason, DateTimeOffset.UtcNow, ct);
            await _jobs.AddEventAsync(new CrawlerJobEvent { JobId = jobId, EventType = eventType, OccurredAt = DateTimeOffset.UtcNow, Details = reason }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist job failure for {JobId}", jobId);
        }
    }

    private static string DetectCrawlerType(string? snapshot) =>
        CrawlerJobService.DetectCrawlerType(snapshot);

    private static WebCrawlerConfiguration? ParseWebSnapshot(string? snapshot) =>
        CrawlerJobService.ParseWebSnapshot(snapshot);

    public override void Dispose()
    {
        foreach (CancellationTokenSource cts in _activeCts.Values)
        {
            try { cts.Cancel(); cts.Dispose(); } catch { }
        }
        _concurrency?.Dispose();
        base.Dispose();
    }
}
