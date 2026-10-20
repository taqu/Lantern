using Lantern.Core.Administration;
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
    ILogger<CrawlerSchedulerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Crawler scheduler started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await CheckSchedulesAsync(stoppingToken); }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Scheduler check failed");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(options.Value.DispatcherPollIntervalSeconds), stoppingToken); }
            catch (OperationCanceledException) { }
        }
    }

    private async Task CheckSchedulesAsync(CancellationToken ct)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IReadOnlyList<CrawlerSchedule> due = await schedules.GetDueSchedulesAsync(now, ct);
        if (due.Count == 0) return;

        logger.LogInformation("Scheduler: {Count} schedule(s) due", due.Count);

        foreach (CrawlerSchedule schedule in due)
        {
            try
            {
                await ProcessDueScheduleAsync(schedule, now, ct);
            }
            catch (Exception ex)
            {
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

        // Retrieve the crawler configuration. If unavailable, skip this occurrence.
        // The caller (CrawlerManagementService) stores configs; we access them via ICrawlStateRepository.
        // We store the config snapshot at job creation. Here we look up the crawler in CrawlerJobService.
        // The schedule doesn't carry the config — look it up from the last submitted snapshot.
        // For Phase 7 file crawlers, we re-use the config from the last submitted job for this crawler.
        string? snapshot = await GetLatestSnapshotAsync(schedule.CrawlerId, ct);
        if (snapshot == null)
        {
            logger.LogWarning("Scheduler: no configuration snapshot found for crawler {CrawlerId}; skipping", schedule.CrawlerId);
            return;
        }

        string crawlerType = CrawlerJobService.DetectCrawlerType(snapshot);
        bool created;

        if (crawlerType == "web")
        {
            WebCrawlerConfiguration? webConfig = CrawlerJobService.ParseWebSnapshot(snapshot);
            if (webConfig == null)
            {
                logger.LogWarning("Scheduler: could not parse web configuration for crawler {CrawlerId}; skipping", schedule.CrawlerId);
                return;
            }
            created = await jobService.SubmitScheduledWebJobAsync(
                schedule.CrawlerId,
                webConfig.Name,
                webConfig,
                schedule.NextRunAt ?? now,
                ct);
        }
        else
        {
            FileCrawlerConfiguration? config = CrawlerJobService.ParseSnapshot(snapshot);
            if (config == null)
            {
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

        if (created) dispatcher.SignalNewJob();
    }

    private async Task<string?> GetLatestSnapshotAsync(Guid crawlerId, CancellationToken ct)
    {
        // Find the most recent job for this crawler to reuse its config snapshot
        (IReadOnlyList<CrawlerJob> page, _) = await jobs.GetJobsPagedAsync(1, 1, crawlerId, ct: ct);
        return page.FirstOrDefault()?.ConfigurationSnapshot;
    }
}
