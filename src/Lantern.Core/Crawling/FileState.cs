namespace Lantern.Core.Crawling;

public sealed class FileState
{
    public long Id { get; set; }
    public Guid CrawlerId { get; set; }
    public string DocumentId { get; set; } = "";
    public string CanonicalPath { get; set; } = "";
    public long FileSize { get; set; }
    public DateTimeOffset LastWriteTimeUtc { get; set; }
    public Guid LastSeenScanId { get; set; }
    public DateTimeOffset? LastIndexedAt { get; set; }
    public CrawlSyncStatus SyncStatus { get; set; }
    public string? LastError { get; set; }
}
