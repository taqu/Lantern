namespace Lantern.Infrastructure.Persistence;

public sealed class CrawledUrlState
{
    public long Id { get; set; }
    public Guid CrawlerId { get; set; }
    public string CanonicalUrl { get; set; } = "";
    public string? DocumentId { get; set; }
    public string? ETag { get; set; }
    public string? LastModified { get; set; }
    public string? ContentHash { get; set; }
    public Guid LastSeenScanId { get; set; }
    public DateTimeOffset? LastFetchedAt { get; set; }
    public DateTimeOffset? LastIndexedAt { get; set; }
    public int? LastHttpStatus { get; set; }
    public string SyncStatus { get; set; } = "Pending";
    public string? LastError { get; set; }
}
