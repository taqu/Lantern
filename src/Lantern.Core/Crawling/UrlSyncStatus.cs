namespace Lantern.Core.Crawling;

public enum UrlSyncStatus
{
    Pending,
    Indexed,
    Unchanged,
    Failed,
    NotFound,
    PendingRetry
}
