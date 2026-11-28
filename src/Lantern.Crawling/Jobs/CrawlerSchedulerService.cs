using Lantern.Core.Crawling;
using Lantern.Core.Jobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lantern.Crawling.Jobs;

/// <summary>
/// Background service that checks for due crawler schedules and submits Pending jobs.
/// Missed occurrences are coalesced into at most one catch-up job per crawler.
/// </summary>
public sealed class CrawlerSchedulerService(
    ICrawlerScheduleRepository schedules,
    ICrawlerJobRepository jobs,
    CrawlerJobService jobService,
    JobDispatcherService dispatcher,
    IOptions<JobOptions> options,
    ILogger<CrawlerSchedulerService> logger) : BackgroundService, ISchedulerHealthService
{
    private const int MaxConsecutiveFailures = 5;
    private const double MaxBackoffSeconds = 30.0;

    private volatile SchedulerStatus _status = new(SchedulerState.Starting, null, null);

    public SchedulerStatus GetStatus() => _status;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Crawler scheduler started");
        _status = new(SchedulerState.Running, null, null);

        int consecutiveFailures = 0;

        while(!stoppingToken.IsCancellationRequested) {
            try {
                await CheckSchedulesAsync(stoppingToken);
                consecutiveFailures = 0;
            } catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) {
                break;
            } catch(Exception ex) when(IsPermanentError(ex)) {
                logger.LogCritical(ex, "Scheduler encountered a permanent error and will stop. Fix the application before restarting.");
                _status = new(SchedulerState.Faulted, ex.Message, DateTimeOffset.UtcNow);
                return;
            } catch(Exception ex) {
                consecutiveFailures++;

                if(consecutiveFailures >= MaxConsecutiveFailures) {
                    logger.LogCritical(ex, "Scheduler failed {Count} consecutive times and will stop", consecutiveFailures);
                    _status = new(SchedulerState.Faulted, ex.Message, DateTimeOffset.UtcNow);
                    return;
                }

                TimeSpan delay = TimeSpan.FromSeconds(Math.Min(
                    options.Value.BackoffBaseSeconds * Math.Pow(2, consecutiveFailures - 1),
                    MaxBackoffSeconds));
                logger.LogWarning(ex, "Scheduler check failed (attempt {Attempt}/{Max}); retrying in {Seconds}s",
                    consecutiveFailures, MaxConsecutiveFailures, delay.TotalSeconds);

                try { await Task.Delay(delay, stoppingToken); } catch(OperationCanceledException) { break; }
                continue;
            }

            try { await Task.Delay(TimeSpan.FromSeconds(options.Value.DispatcherPollIntervalSeconds), stoppingToken); } catch(OperationCanceledException) { break; }
        }

        _status = new(SchedulerState.Stopped, null, null);
        logger.LogInformation("Crawler scheduler stopped");
    }

    private static bool IsPermanentError(Exception ex)
    {
        if(ex is InvalidOperationException) {
            string msg = ex.Message;
            if(msg.Contains("could not be translated", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("translation", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("LINQ", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private async Task CheckSchedulesAsync(CancellationToken ct)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IReadOnlyList<CrawlerSchedule> due = await schedules.GetDueSchedulesAsync(now, ct);
        if(due.Count == 0) return;

        logger.LogInformation("Scheduler: {Count} schedule(s) due", due.Count);

        foreach(CrawlerSchedule schedule in due) {
            try {
                await ProcessDueScheduleAsync(schedule, now, ct);
            } catch(OperationCanceledException) when(ct.IsCancellationRequested) {
                throw;
            } catch(Exception ex) {
                logger.LogError(ex, "Failed to process schedule for crawler {CrawlerId}", schedule.CrawlerId);
            }
        }
    }

    private async Task ProcessDueScheduleAsync(CrawlerSchedule schedule, DateTimeOffset now, CancellationToken ct)
    {
        // Calculate the next run time BEFORE creating the job, so that subsequent triggers are correct.
        DateTimeOffset nextRun = JobScheduleCalculator.CalculateNextRun(schedule, now) ?? now.AddDays(1);

        // Advance the schedule first to prevent double-firing if job creation fails partway through.
        await schedules.UpdateLastTriggeredAsync(schedule.CrawlerId, now, nextRun, ct);

        string? snapshot = await GetLatestSnapshotAsync(schedule.CrawlerId, ct);
        if(snapshot == null) {
            logger.LogWarning("Scheduler: no configuration snapshot found for crawler {CrawlerId}; skipping", schedule.CrawlerId);
            return;
        }

        string crawlerType = CrawlerJobService.DetectCrawlerType(snapshot);
        bool created;

        if(crawlerType == "web") {
            WebCrawlerConfiguration? webConfig = CrawlerJobService.ParseWebSnapshot(snapshot);
            if(webConfig == null) {
                logger.LogWarning("Scheduler: could not parse web configuration for crawler {CrawlerId}; skipping", schedule.CrawlerId);
                return;
            }
            created = await jobService.SubmitScheduledWebJobAsync(
                schedule.CrawlerId,
                webConfig.Name,
                webConfig,
                schedule.NextRunAt ?? now,
                ct);
        } else {
            FileCrawlerConfiguration? config = CrawlerJobService.ParseSnapshot(snapshot);
            if(config == null) {
                logger.LogWarning("Scheduler: could not parse configuration for crawler {CrawlerId}; skipping", schedule.CrawlerId);
                return;
            }
            created = await jobService.SubmitScheduledJobAsync(
                schedule.CrawlerId,
                config.Name,
                config,
                schedule.NextRunAt ?? now,
                ct);
        }

        if(created) dispatcher.SignalNewJob();
    }

    private async Task<string?> GetLatestSnapshotAsync(Guid crawlerId, CancellationToken ct)
    {
        (IReadOnlyList<CrawlerJob> page, _) = await jobs.GetJobsPagedAsync(1, 1, crawlerId, ct: ct);
        return page.FirstOrDefault()?.ConfigurationSnapshot;
    }
}
