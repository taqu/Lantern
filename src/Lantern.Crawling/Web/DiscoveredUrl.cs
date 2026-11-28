namespace Lantern.Crawling.Web;

public sealed record DiscoveredUrl(string CanonicalUrl, int Depth, string? ParentUrl = null);
