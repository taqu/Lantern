using System.ComponentModel.DataAnnotations;

namespace Lantern.Core.Crawling;

public sealed class WebCrawlerConfiguration
{
    public string CrawlerType { get; init; } = "web";
    public Guid CrawlerId { get; init; }
    public string Name { get; init; } = "";
    public IReadOnlyList<string> StartUrls { get; init; } = [];
    public IReadOnlyList<string> AllowedHosts { get; init; } = [];
    public IReadOnlyList<string> AllowedPathPrefixes { get; init; } = ["/"];
    public IReadOnlyList<string> ExcludeUrlPatterns { get; init; } = [];
    public int MaximumDepth { get; init; } = 5;
    public int MaximumPages { get; init; } = 10000;
    public int RequestDelayMilliseconds { get; init; } = 500;
    public int MaximumConcurrentRequests { get; init; } = 2;
    public int MaximumResponseSizeMB { get; init; } = 100;
    public bool FollowRedirects { get; init; } = true;
    public bool RespectRobotsTxt { get; init; } = true;
    public bool IndexLinkedDocuments { get; init; } = true;

    public void Validate()
    {
        if (CrawlerId == Guid.Empty)
            throw new ValidationException("Crawler ID is required.");
        if (StartUrls.Count == 0)
            throw new ValidationException("At least one start URL is required.");
        if (AllowedHosts.Count == 0)
            throw new ValidationException("At least one allowed host is required.");
        foreach (string url in StartUrls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? u)
                || (u.Scheme != "http" && u.Scheme != "https")
                || !string.IsNullOrEmpty(u.UserInfo))
                throw new ValidationException($"Invalid start URL: {url}");
        }
        if (MaximumDepth <= 0) throw new ValidationException("MaximumDepth must be positive.");
        if (MaximumPages <= 0) throw new ValidationException("MaximumPages must be positive.");
        if (MaximumConcurrentRequests <= 0 || MaximumConcurrentRequests > 16)
            throw new ValidationException("MaximumConcurrentRequests must be between 1 and 16.");
        if (MaximumResponseSizeMB <= 0 || MaximumResponseSizeMB > 1000)
            throw new ValidationException("MaximumResponseSizeMB must be between 1 and 1000.");
    }
}
