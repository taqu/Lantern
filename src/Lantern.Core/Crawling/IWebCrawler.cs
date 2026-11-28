namespace Lantern.Core.Crawling;

public interface IWebCrawler
{
    Task<FileCrawlResult> CrawlAsync(WebCrawlerConfiguration config, CancellationToken cancellationToken = default);
}
