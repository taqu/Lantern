using Lantern.Core.Jobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lantern.Crawling.Jobs;

/// <summary>
/// Periodically removes old terminal job records and events beyond the configured retention window.
/// Never deletes active, pending, or schedule records.
/// </summary>
public sealed class RetentionCleanupService(
    ICrawlerJobRepository jobs,
    IOptions<JobOptions> options,
    ILogger<RetentionCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Delay initial run by 10 minutes to avoid startup contention
        try { await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken); }
        catch (OperationCanceledException) { }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunCleanupAsync(stoppingToken); }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Retention cleanup failed");
            }

            try { await Task.Delay(TimeSpan.FromHours(6), stoppingToken); }
            catch (OperationCanceledException) { }
        }
    }

    private async Task RunCleanupAsync(CancellationToken ct)
    {
        JobOptions opts = options.Value;
        DateTimeOffset jobCutoff = DateTimeOffset.UtcNow.AddDays(-opts.HistoryRetentionDays);
        DateTimeOffset eventCutoff = DateTimeOffset.UtcNow.AddDays(-opts.EventRetentionDays);

        int deletedJobs = await jobs.DeleteOldJobsAsync(jobCutoff, ct);
        int deletedEvents = await jobs.DeleteOldEventsAsync(eventCutoff, ct);

        if (deletedJobs > 0 || deletedEvents > 0)
            logger.LogInformation("Retention cleanup: removed {Jobs} job(s), {Events} event(s)", deletedJobs, deletedEvents);
    }
}
