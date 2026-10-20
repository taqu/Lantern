using Lantern.Core.Administration;

namespace Lantern.Infrastructure.Persistence;

public sealed class WebCrawlerProfileRecord
{
    public Guid CrawlerId { get; set; }
    public string StartUrls { get; set; } = "";
    public string AllowedHosts { get; set; } = "";
    public string? AllowedPathPrefixes { get; set; }
    public string? ExcludeUrlPatterns { get; set; }
    public int MaximumDepth { get; set; } = 5;
    public int MaximumPages { get; set; } = 10000;
    public int RequestDelayMilliseconds { get; set; } = 500;
    public int MaximumConcurrentRequests { get; set; } = 2;
    public int MaximumResponseSizeMB { get; set; } = 100;
    public bool FollowRedirects { get; set; } = true;
    public bool RespectRobotsTxt { get; set; } = true;
    public bool IndexLinkedDocuments { get; set; } = true;

    public WebCrawlerProfile ToDomain() => new()
    {
        CrawlerId = CrawlerId,
        StartUrls = Split(StartUrls),
        AllowedHosts = Split(AllowedHosts),
        AllowedPathPrefixes = Split(AllowedPathPrefixes) is { Count: > 0 } p ? p : ["/"],
        ExcludeUrlPatterns = Split(ExcludeUrlPatterns),
        MaximumDepth = MaximumDepth,
        MaximumPages = MaximumPages,
        RequestDelayMilliseconds = RequestDelayMilliseconds,
        MaximumConcurrentRequests = MaximumConcurrentRequests,
        MaximumResponseSizeMB = MaximumResponseSizeMB,
        FollowRedirects = FollowRedirects,
        RespectRobotsTxt = RespectRobotsTxt,
        IndexLinkedDocuments = IndexLinkedDocuments
    };

    public static WebCrawlerProfileRecord FromDomain(WebCrawlerProfile profile) => new()
    {
        CrawlerId = profile.CrawlerId,
        StartUrls = Join(profile.StartUrls),
        AllowedHosts = Join(profile.AllowedHosts),
        AllowedPathPrefixes = profile.AllowedPathPrefixes.Count > 0 ? Join(profile.AllowedPathPrefixes) : null,
        ExcludeUrlPatterns = profile.ExcludeUrlPatterns.Count > 0 ? Join(profile.ExcludeUrlPatterns) : null,
        MaximumDepth = profile.MaximumDepth,
        MaximumPages = profile.MaximumPages,
        RequestDelayMilliseconds = profile.RequestDelayMilliseconds,
        MaximumConcurrentRequests = profile.MaximumConcurrentRequests,
        MaximumResponseSizeMB = profile.MaximumResponseSizeMB,
        FollowRedirects = profile.FollowRedirects,
        RespectRobotsTxt = profile.RespectRobotsTxt,
        IndexLinkedDocuments = profile.IndexLinkedDocuments
    };

    private static List<string> Split(string? value) =>
        string.IsNullOrWhiteSpace(value) ? [] :
        [.. value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static string Join(IEnumerable<string> values) => string.Join('\n', values);
}
