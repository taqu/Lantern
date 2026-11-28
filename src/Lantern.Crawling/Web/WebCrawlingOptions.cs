namespace Lantern.Crawling.Web;

public sealed class WebCrawlingOptions
{
    public const string SectionName = "WebCrawling";

    public int RequestTimeoutSeconds { get; init; } = 30;
    public int MaximumRedirects { get; init; } = 5;
    public int MaximumHtmlSizeMB { get; init; } = 10;
    public int MaximumPendingUrls { get; init; } = 100_000;
    public string UserAgent { get; init; } = "LanternCrawler/0.8 (+https://github.com/lantern)";
    public int RobotsRefreshHours { get; init; } = 24;
}
