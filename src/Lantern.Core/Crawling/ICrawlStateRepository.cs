namespace Lantern.Core.Crawling;

public interface ICrawlStateRepository
{
    Task<FileState?> FindAsync(Guid crawlerId, string canonicalPath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FileState>> GetNotSeenInScanAsync(Guid crawlerId, Guid scanId, CancellationToken cancellationToken = default);
    Task SaveAsync(FileState state, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}
