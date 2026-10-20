using Lantern.Core.Administration;
using Lantern.Core.Crawling;
using Lantern.Core.Extraction;
using Lantern.Core.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lantern.Crawling.Web;

/// <summary>
/// Web crawler orchestrator: BFS URL discovery, robots.txt, rate limiting,
/// HTTP fetching, content extraction, and incremental synchronization.
/// Integrates with Phase 7 persistent job infrastructure via IWebCrawler.
/// JavaScript execution is not supported — pages requiring JS may have incomplete content.
/// </summary>
public sealed class WebCrawler(
    ICrawledUrlStateRepository urlStates,
    IDocumentIndexingService documentIndexing,
    IChunkIndexingService chunkIndexing,
    IDocumentProcessingService processingService,
    ICrawlerCategoryResolver categoryResolver,
    IOptions<WebCrawlingOptions> options,
    ILoggerFactory loggerFactory) : IWebCrawler
{
    private readonly WebCrawlingOptions _options = options.Value;
    private readonly ILogger _logger = loggerFactory.CreateLogger<WebCrawler>();

    public async Task<FileCrawlResult> CrawlAsync(WebCrawlerConfiguration config, CancellationToken ct = default)
    {
        config.Validate();

        Guid scanId = Guid.NewGuid();
        var validator = new CrawlBoundaryValidator();
        var limiter = new HostRateLimiter(config.RequestDelayMilliseconds, config.MaximumConcurrentRequests);
        var synchronizer = new WebContentSynchronizer(processingService, documentIndexing, chunkIndexing, urlStates, categoryResolver, loggerFactory.CreateLogger<WebContentSynchronizer>());
        var reconciler = new WebDeletionReconciler(urlStates, documentIndexing, chunkIndexing, loggerFactory.CreateLogger<WebDeletionReconciler>());
        var counters = new CrawlCounters();

        using var fetcher = new HttpFetcher(config, _options, validator, _logger);
        RobotsPolicy? robots = config.RespectRobotsTxt
            ? new RobotsPolicy(_options.UserAgent, _logger)
            : null;

        // BFS frontier and deduplication
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var frontier = new Queue<DiscoveredUrl>();
        bool pageLimitReached = false;
        bool canceled = false;

        // Seed the frontier
        foreach (string startUrl in config.StartUrls)
        {
            string? norm = UrlNormalizer.TryNormalize(startUrl);
            if (norm != null && validator.IsUrlInScope(norm, config) && seen.Add(norm))
            {
                frontier.Enqueue(new DiscoveredUrl(norm, 0));
                counters.Discovered++;
            }
        }

        while (frontier.Count > 0 && !ct.IsCancellationRequested)
        {
            if (counters.Fetched >= config.MaximumPages)
            {
                pageLimitReached = true;
                _logger.LogInformation("Page limit of {Limit} reached", config.MaximumPages);
                break;
            }

            DiscoveredUrl current = frontier.Dequeue();
            counters.Fetched++;

            try
            {
                await ProcessUrlAsync(current, config, fetcher, robots, validator, limiter,
                    synchronizer, reconciler, frontier, seen, scanId, counters, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                canceled = true;
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error processing {Url}", current.CanonicalUrl);
                counters.Failed++;
                counters.Errors.Add($"{current.CanonicalUrl}: {ex.Message}");
            }
        }

        if (ct.IsCancellationRequested) canceled = true;

        // Deletion reconciliation — only when scan completed without limits or cancellation
        if (!canceled && !pageLimitReached)
        {
            try { counters.Deleted = await reconciler.ReconcileAsync(config.CrawlerId, scanId, ct); }
            catch (Exception ex) { _logger.LogError(ex, "Deletion reconciliation failed"); }
        }
        else
        {
            _logger.LogInformation("Skipping deletion reconciliation: canceled={C}, limitReached={P}", canceled, pageLimitReached);
        }

        limiter.Dispose();

        CrawlStatus status = canceled ? CrawlStatus.Canceled
            : counters.Failed > 0 ? CrawlStatus.CompletedWithErrors
            : CrawlStatus.Completed;

        return new FileCrawlResult
        {
            DiscoveredFiles = (int)counters.Discovered,
            ProcessedFiles = (int)counters.Fetched,
            IndexedFiles = (int)counters.Indexed,
            UnchangedFiles = (int)counters.Unchanged,
            SkippedFiles = (int)counters.Skipped,
            FailedFiles = (int)counters.Failed,
            DeletedFiles = (int)counters.Deleted,
            Status = status,
            Errors = counters.Errors,
            FinishedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task ProcessUrlAsync(
        DiscoveredUrl current,
        WebCrawlerConfiguration config,
        HttpFetcher fetcher,
        RobotsPolicy? robots,
        CrawlBoundaryValidator validator,
        HostRateLimiter limiter,
        WebContentSynchronizer synchronizer,
        WebDeletionReconciler reconciler,
        Queue<DiscoveredUrl> frontier,
        HashSet<string> seen,
        Guid scanId,
        CrawlCounters counters,
        CancellationToken ct)
    {
        string url = current.CanonicalUrl;
        _logger.LogDebug("Processing {Url} (depth={Depth})", url, current.Depth);

        // robots.txt check
        if (robots != null && !await robots.IsAllowedAsync(url, fetcher.HttpClient, ct))
        {
            _logger.LogDebug("robots.txt disallowed {Url}", url);
            counters.Skipped++;
            return;
        }

        // Per-host rate limiting
        await limiter.WaitAsync(url, ct);

        // Load existing state for conditional requests
        UrlState? existing = await urlStates.FindAsync(config.CrawlerId, url, ct);
        string? etag = existing?.ETag;
        string? lastModified = existing?.LastModified;

        // Follow redirects (up to MaximumRedirects)
        string fetchUrl = url;
        int redirectCount = 0;
        FetchResult result;

        while (true)
        {
            result = await fetcher.FetchAsync(fetchUrl, etag, lastModified, ct);

            if (result.Status != FetchStatus.Redirect || !config.FollowRedirects) break;
            if (redirectCount >= _options.MaximumRedirects)
            {
                _logger.LogWarning("Redirect limit exceeded for {Url}", url);
                counters.Skipped++;
                return;
            }

            string? redirectTarget = result.RedirectUrl;
            if (string.IsNullOrEmpty(redirectTarget)) { counters.Skipped++; return; }

            // Normalize and validate redirect target
            string? normRedirect = UrlNormalizer.TryNormalize(redirectTarget, fetchUrl);
            if (normRedirect == null || !validator.IsUrlInScope(normRedirect, config))
            {
                _logger.LogDebug("Redirect to out-of-scope URL from {Url}: {Target}", url, redirectTarget);
                counters.Skipped++;
                return;
            }

            fetchUrl = normRedirect;
            redirectCount++;
        }

        switch (result.Status)
        {
            case FetchStatus.NotModified:
                await synchronizer.MarkNotModifiedAsync(url, config.CrawlerId, scanId, ct);
                counters.Unchanged++;
                return;

            case FetchStatus.NotFound:
            case FetchStatus.Gone:
                reconciler.MarkGone(url);
                if (existing != null)
                {
                    existing.LastSeenScanId = scanId;
                    existing.LastHttpStatus = result.HttpStatusCode;
                    existing.SyncStatus = UrlSyncStatus.NotFound;
                    existing.LastFetchedAt = DateTimeOffset.UtcNow;
                    await urlStates.SaveAsync(existing, ct);
                }
                return;

            case FetchStatus.SsrfBlocked:
                _logger.LogWarning("SSRF blocked {Url}: {Reason}", url, result.ErrorMessage);
                counters.Skipped++;
                return;

            case FetchStatus.TooLarge:
                _logger.LogWarning("Response too large for {Url}", url);
                counters.Skipped++;
                return;

            case FetchStatus.TooManyRequests:
                _logger.LogWarning("HTTP 429 for {Url}", url);
                counters.Skipped++;
                return;

            case FetchStatus.NetworkError:
                _logger.LogWarning("Network error for {Url}: {Msg}", url, result.ErrorMessage);
                counters.Failed++;
                counters.Errors.Add($"{url}: {result.ErrorMessage}");
                return;

            case FetchStatus.Ok when result.Body != null:
                break;

            default:
                counters.Skipped++;
                return;
        }

        string contentType = result.ContentType ?? "";
        bool isHtml = IsHtml(contentType);

        // Discover and enqueue links from HTML
        if (isHtml && current.Depth < config.MaximumDepth)
        {
            IReadOnlyList<string> links = LinkExtractor.ExtractLinks(result.Body!, url);
            foreach (string link in links)
            {
                if (!validator.IsUrlInScope(link, config)) continue;
                if (!seen.Add(link)) continue;
                frontier.Enqueue(new DiscoveredUrl(link, current.Depth + 1, url));
                counters.Discovered++;
            }
        }

        // Decide whether to index
        bool shouldIndex = isHtml || (config.IndexLinkedDocuments && IsSupportedDocument(contentType));
        if (!shouldIndex) { counters.Skipped++; return; }

        (WebSyncOutcome outcome, string? error) = await synchronizer.SyncAsync(
            url, result.Body!, contentType, result.ETag, result.LastModified,
            config.CrawlerId, scanId, ct);

        switch (outcome)
        {
            case WebSyncOutcome.Indexed: counters.Indexed++; break;
            case WebSyncOutcome.Updated: counters.Indexed++; break;
            case WebSyncOutcome.Unchanged: counters.Unchanged++; break;
            case WebSyncOutcome.Failed:
                counters.Failed++;
                if (error != null) counters.Errors.Add($"{url}: {error}");
                break;
            default: counters.Skipped++; break;
        }
    }

    private static bool IsHtml(string contentType)
    {
        string ct = contentType.Split(';')[0].Trim().ToLowerInvariant();
        return ct is "text/html" or "application/xhtml+xml";
    }

    private static bool IsSupportedDocument(string contentType)
    {
        string ct = contentType.Split(';')[0].Trim().ToLowerInvariant();
        return ct is "application/pdf"
            or "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            or "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            or "application/vnd.openxmlformats-officedocument.presentationml.presentation"
            or "text/plain"
            or "text/markdown"
            or "text/x-markdown";
    }

    private sealed class CrawlCounters
    {
        public long Discovered;
        public long Fetched;
        public long Indexed;
        public long Unchanged;
        public long Skipped;
        public long Failed;
        public long Deleted;
        public List<string> Errors { get; } = [];
    }
}
