using Lantern.Core.Crawling;
using Lantern.Core.Search;
using Microsoft.Extensions.Logging;

namespace Lantern.Crawling.Web;

/// <summary>
/// Conservatively reconciles previously indexed URLs that were not seen in the current scan.
/// Only removes URLs that received a confirmed 404/410 in the current scan.
/// Never deletes based solely on absence from discovery — a URL may be reachable
/// but unreached due to page limits, robots.txt, or partial discovery.
/// </summary>
public sealed class WebDeletionReconciler(
    ICrawledUrlStateRepository stateRepository,
    IDocumentIndexingService documentIndexing,
    IChunkIndexingService chunkIndexing,
    ILogger<WebDeletionReconciler> logger)
{
    // Set of URLs confirmed gone (404/410) in the current scan
    private readonly HashSet<string> _confirmedGone = new(StringComparer.OrdinalIgnoreCase);

    public void MarkGone(string canonicalUrl) => _confirmedGone.Add(canonicalUrl);

    /// <summary>
    /// Reconcile only URLs that were confirmed gone (404/410) AND were not re-indexed.
    /// Never performs bulk deletion from scan absence alone.
    /// </summary>
    public async Task<int> ReconcileAsync(Guid crawlerId, Guid scanId, CancellationToken ct)
    {
        if (_confirmedGone.Count == 0) return 0;

        IReadOnlyList<UrlState> notSeen = await stateRepository.GetNotSeenInScanAsync(crawlerId, scanId, ct);
        int deleted = 0;

        foreach (UrlState state in notSeen)
        {
            if (!_confirmedGone.Contains(state.CanonicalUrl)) continue;
            if (string.IsNullOrEmpty(state.DocumentId)) continue;

            try
            {
                await chunkIndexing.DeleteChunksByDocumentAsync(state.DocumentId, ct);
                await documentIndexing.DeleteAsync(state.DocumentId, ct);
                await stateRepository.DeleteAsync(state.Id, ct);
                deleted++;
                logger.LogInformation("Removed confirmed-gone URL {Url}", state.CanonicalUrl);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete indexed content for {Url}", state.CanonicalUrl);
            }
        }

        return deleted;
    }
}
