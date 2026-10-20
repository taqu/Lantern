using Lantern.Core.Crawling;
using Lantern.Core.Search;
using Lantern.Crawling.Files;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Lantern.Crawling;

public sealed class FileCrawler(
    DirectoryEnumerator enumerator,
    FileSynchronizationService syncService,
    DeletionReconciler reconciler,
    IOptions<FileCrawlingOptions> options,
    ILogger<FileCrawler> logger) : IFileCrawler
{
    public async Task<FileCrawlResult> CrawlAsync(
        FileCrawlerConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        configuration.Validate();

        var scanId = Guid.NewGuid();
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        var errors = new ConcurrentBag<string>();

        int discovered = 0, unchanged = 0, indexed = 0, updated = 0, failed = 0, skipped = 0;

        logger.LogInformation("Crawl {ScanId} starting for crawler {CrawlerId} at {RootPath}",
            scanId, configuration.CrawlerId, configuration.RootPath);

        // Normalize root path
        string canonicalRoot;
        try { canonicalRoot = DocumentIdentity.FilePath(configuration.RootPath); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Invalid root path {RootPath}", configuration.RootPath);
            return BuildResult(configuration.CrawlerId, scanId, CrawlStatus.Failed,
                startedAt, 0, 0, 0, 0, 0, 0, 0, [ex.Message]);
        }

        // Check root accessibility
        if (!Directory.Exists(canonicalRoot))
        {
            string msg = $"Root path does not exist or is unavailable: {canonicalRoot}";
            logger.LogError("{Message}", msg);
            return BuildResult(configuration.CrawlerId, scanId, CrawlStatus.Failed,
                startedAt, 0, 0, 0, 0, 0, 0, 0, [msg]);
        }

        var filter = new FileFilter(configuration);
        var enumState = new EnumerationState();

        var channelOptions = new BoundedChannelOptions(options.Value.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = false
        };
        var channel = Channel.CreateBounded<DiscoveredFile>(channelOptions);

        // Producer: stream files into channel
        var producerTask = Task.Run(async () =>
        {
            try
            {
                await foreach (DiscoveredFile file in enumerator.EnumerateAsync(canonicalRoot, filter, enumState, cancellationToken))
                {
                    Interlocked.Increment(ref discovered);
                    await channel.Writer.WriteAsync(file, cancellationToken);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                enumState.RootFailure = ex;
                errors.Add($"Enumeration error: {ex.Message}");
                logger.LogError(ex, "Enumeration failed for {Root}", canonicalRoot);
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, CancellationToken.None);

        // Consumers: process files with bounded concurrency
        Task[] workers = Enumerable.Range(0, configuration.MaxConcurrentFiles).Select(_ =>
            Task.Run(async () =>
            {
                await foreach (DiscoveredFile file in channel.Reader.ReadAllAsync(CancellationToken.None))
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    try
                    {
                        (FileSyncOutcome outcome, string error) = await syncService.SyncAsync(
                            file, configuration.CrawlerId, scanId, cancellationToken);

                        switch (outcome)
                        {
                            case FileSyncOutcome.Unchanged: Interlocked.Increment(ref unchanged); break;
                            case FileSyncOutcome.Indexed:   Interlocked.Increment(ref indexed);   break;
                            case FileSyncOutcome.Updated:   Interlocked.Increment(ref updated);   break;
                            case FileSyncOutcome.Failed:
                                Interlocked.Increment(ref failed);
                                if (error != null) errors.Add($"{file.CanonicalPath}: {error}");
                                break;
                            case FileSyncOutcome.Skipped:   Interlocked.Increment(ref skipped);   break;
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failed);
                        errors.Add($"{file.CanonicalPath}: {ex.Message}");
                        logger.LogError(ex, "Unhandled error processing {Path}", file.CanonicalPath);
                    }
                }
            })).ToArray();

        await producerTask;
        await Task.WhenAll(workers);

        bool wasCanceled = cancellationToken.IsCancellationRequested;
        if (wasCanceled)
        {
            logger.LogInformation("Crawl {ScanId} canceled", scanId);
            return BuildResult(configuration.CrawlerId, scanId, CrawlStatus.Canceled,
                startedAt, discovered, indexed + updated, indexed, unchanged, skipped, failed, 0, [.. errors]);
        }

        if (enumState.RootFailure != null)
        {
            logger.LogError("Crawl {ScanId} failed due to root enumeration failure", scanId);
            return BuildResult(configuration.CrawlerId, scanId, CrawlStatus.Failed,
                startedAt, discovered, indexed + updated, indexed, unchanged, skipped, failed, 0, [.. errors]);
        }

        // Add directory errors but allow reconciliation if enumeration itself completed
        foreach (string dirError in enumState.DirectoryErrors)
            errors.Add(dirError);

        int deletedFiles = 0;
        if (enumState.Completed)
        {
            (int deleted, List<string> reconcileErrors) = await reconciler.ReconcileAsync(
                configuration.CrawlerId, scanId, CancellationToken.None);
            deletedFiles = deleted;
            foreach (string e in reconcileErrors) errors.Add(e);
        }
        else
        {
            logger.LogWarning("Crawl {ScanId}: enumeration did not complete — skipping deletion reconciliation", scanId);
        }

        CrawlStatus status = errors.IsEmpty ? CrawlStatus.Completed : CrawlStatus.CompletedWithErrors;
        logger.LogInformation(
            "Crawl {ScanId} {Status}: discovered={D} indexed={I} updated={U} unchanged={S} failed={F} deleted={Del}",
            scanId, status, discovered, indexed, updated, unchanged, failed, deletedFiles);

        return BuildResult(configuration.CrawlerId, scanId, status,
            startedAt, discovered, indexed + updated, indexed, unchanged, skipped, failed, deletedFiles, [.. errors]);
    }

    private static FileCrawlResult BuildResult(
        Guid crawlerId, Guid scanId, CrawlStatus status, DateTimeOffset startedAt,
        int discovered, int processed, int newlyIndexed, int unchanged,
        int skipped, int failed, int deleted, IReadOnlyList<string> errors) =>
        new()
        {
            CrawlerId = crawlerId,
            ScanId = scanId,
            Status = status,
            DiscoveredFiles = discovered,
            ProcessedFiles = processed,
            IndexedFiles = newlyIndexed,
            UnchangedFiles = unchanged,
            SkippedFiles = skipped,
            FailedFiles = failed,
            DeletedFiles = deleted,
            StartedAt = startedAt,
            FinishedAt = DateTimeOffset.UtcNow,
            Errors = errors
        };
}
