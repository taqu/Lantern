namespace Lantern.Crawling.Files;

public enum FileSyncOutcome
{
    Unchanged,
    Indexed,
    Updated,
    Failed,
    Skipped,
    // Extraction and parent-document indexing submitted; chunk indexing deferred because
    // the REST embedder is not yet verified. The crawler will retry automatically on the
    // next scan once HybridSearchReadiness becomes Ready.
    Deferred
}
