using Lantern.Core.Crawling;

namespace Lantern.Core.Administration;

public sealed class WebCrawlerProfile
{
    public Guid CrawlerId { get; set; }
    public List<string> StartUrls { get; set; } = [];
    public List<string> AllowedHosts { get; set; } = [];
    public List<string> AllowedPathPrefixes { get; set; } = ["/"];
    public List<string> ExcludeUrlPatterns { get; set; } = [];
    public int MaximumDepth { get; set; } = 5;
    public int MaximumPages { get; set; } = 10000;
    public int RequestDelayMilliseconds { get; set; } = 500;
    public int MaximumConcurrentRequests { get; set; } = 2;
    public int MaximumResponseSizeMB { get; set; } = 100;
    public bool FollowRedirects { get; set; } = true;
    public bool RespectRobotsTxt { get; set; } = true;
    public bool IndexLinkedDocuments { get; set; } = true;

    public WebCrawlerConfiguration ToConfiguration(string name) => new()
    {
        CrawlerId = CrawlerId,
        Name = name,
        StartUrls = [.. StartUrls],
        AllowedHosts = [.. AllowedHosts],
        AllowedPathPrefixes = AllowedPathPrefixes.Count > 0 ? [.. AllowedPathPrefixes] : ["/"],
        ExcludeUrlPatterns = [.. ExcludeUrlPatterns],
        MaximumDepth = MaximumDepth,
        MaximumPages = MaximumPages,
        RequestDelayMilliseconds = RequestDelayMilliseconds,
        MaximumConcurrentRequests = MaximumConcurrentRequests,
        MaximumResponseSizeMB = MaximumResponseSizeMB,
        FollowRedirects = FollowRedirects,
        RespectRobotsTxt = RespectRobotsTxt,
        IndexLinkedDocuments = IndexLinkedDocuments
    };
}
