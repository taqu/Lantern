namespace Lantern.Core.Crawling;

public interface IFileCrawler
{
    Task<FileCrawlResult> CrawlAsync(FileCrawlerConfiguration configuration, CancellationToken cancellationToken = default);
}
