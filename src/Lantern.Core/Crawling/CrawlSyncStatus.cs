namespace Lantern.Core.Crawling;

public enum CrawlSyncStatus
{
    Indexed,
    Failed,
    PendingRetry,
    // Document upsert task has been submitted to Meilisearch but not yet confirmed.
    // The reconciliation service will transition this to Indexed or Failed.
    IndexingPending
}
