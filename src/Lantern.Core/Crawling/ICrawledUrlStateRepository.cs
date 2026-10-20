namespace Lantern.Core.Crawling;

public interface ICrawledUrlStateRepository
{
    Task<UrlState?> FindAsync(Guid crawlerId, string canonicalUrl, CancellationToken ct = default);
    Task<IReadOnlyList<UrlState>> GetNotSeenInScanAsync(Guid crawlerId, Guid scanId, CancellationToken ct = default);
    Task SaveAsync(UrlState state, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
    Task DeleteAllForCrawlerAsync(Guid crawlerId, CancellationToken ct = default);
}
