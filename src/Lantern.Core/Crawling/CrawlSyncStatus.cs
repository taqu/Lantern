namespace Lantern.Core.Crawling;

public enum CrawlSyncStatus
{
    Indexed,
    Failed,
    PendingRetry,
    // Document upsert task has been submitted to Meilisearch but not yet confirmed.
    // The reconciliation service will transition this to Indexed or Failed.
    IndexingPending,
    // Extraction and parent-document indexing completed; chunk indexing deferred because
    // the REST embedder has not yet been verified. Recovery is automatic: the next crawl
    // scan re-processes this file once HybridSearchReadiness becomes Ready.
    WaitingForEmbeddingReadiness
}
