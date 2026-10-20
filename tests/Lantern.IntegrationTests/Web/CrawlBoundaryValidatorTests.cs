using Lantern.Core.Crawling;
using Lantern.Crawling.Web;

namespace Lantern.IntegrationTests.Web;

public class CrawlBoundaryValidatorTests
{
    private static WebCrawlerConfiguration MakeConfig(
        string[]? hosts = null,
        string[]? pathPrefixes = null,
        string[]? excludePatterns = null) => new()
    {
        CrawlerId = Guid.NewGuid(),
        Name = "test",
        StartUrls = ["https://example.com/"],
        AllowedHosts = hosts ?? ["example.com"],
        AllowedPathPrefixes = pathPrefixes ?? ["/"],
        ExcludeUrlPatterns = excludePatterns ?? [],
    };

    // --- IsHostAllowed ---

    [Fact]
    public void IsHostAllowed_AllowedHost_ReturnsTrue()
    {
        bool result = CrawlBoundaryValidator.IsHostAllowed("example.com", ["example.com"]);
        Assert.True(result);
    }

    [Fact]
    public void IsHostAllowed_UnlistedHost_ReturnsFalse()
    {
        bool result = CrawlBoundaryValidator.IsHostAllowed("other.com", ["example.com"]);
        Assert.False(result);
    }

    [Fact]
    public void IsHostAllowed_CaseInsensitiveMatch()
    {
        bool result = CrawlBoundaryValidator.IsHostAllowed("EXAMPLE.COM", ["example.com"]);
        Assert.True(result);
    }

    // --- IsPathAllowed ---

    [Fact]
    public void IsPathAllowed_PrefixMatchAllowsSubPath()
    {
        bool result = CrawlBoundaryValidator.IsPathAllowed("/docs/page.html", ["/docs"]);
        Assert.True(result);
    }

    [Fact]
    public void IsPathAllowed_PrefixDoesNotMatchSimilarPrefix()
    {
        // /docs should NOT allow /docs-private/page.html
        bool result = CrawlBoundaryValidator.IsPathAllowed("/docs-private/page.html", ["/docs"]);
        Assert.False(result);
    }

    [Fact]
    public void IsPathAllowed_ExactPrefixMatch()
    {
        bool result = CrawlBoundaryValidator.IsPathAllowed("/docs", ["/docs"]);
        Assert.True(result);
    }

    [Fact]
    public void IsPathAllowed_RootPrefixAllowsEverything()
    {
        bool result = CrawlBoundaryValidator.IsPathAllowed("/any/deep/path.html", ["/"]);
        Assert.True(result);
    }

    [Fact]
    public void IsPathAllowed_EmptyListAllowsAll()
    {
        bool result = CrawlBoundaryValidator.IsPathAllowed("/anything", []);
        Assert.True(result);
    }

    // --- IsPatternExcluded ---

    [Fact]
    public void IsPatternExcluded_MatchingPatternExcludesUrl()
    {
        bool result = CrawlBoundaryValidator.IsPatternExcluded("https://example.com/login?redirect=/home", ["login"]);
        Assert.True(result);
    }

    [Fact]
    public void IsPatternExcluded_EmptyPatternListAllowsUrl()
    {
        bool result = CrawlBoundaryValidator.IsPatternExcluded("https://example.com/page", []);
        Assert.False(result);
    }

    // --- IsUrlInScope ---

    [Fact]
    public void IsUrlInScope_AllowedHostAndPath_ReturnsTrue()
    {
        var validator = new CrawlBoundaryValidator();
        var config = MakeConfig(hosts: ["example.com"], pathPrefixes: ["/docs"]);
        bool result = validator.IsUrlInScope("https://example.com/docs/page.html", config);
        Assert.True(result);
    }

    [Fact]
    public void IsUrlInScope_WrongHost_ReturnsFalse()
    {
        var validator = new CrawlBoundaryValidator();
        var config = MakeConfig(hosts: ["example.com"]);
        bool result = validator.IsUrlInScope("https://other.com/page.html", config);
        Assert.False(result);
    }

    [Fact]
    public void IsUrlInScope_WrongPath_ReturnsFalse()
    {
        var validator = new CrawlBoundaryValidator();
        var config = MakeConfig(hosts: ["example.com"], pathPrefixes: ["/docs"]);
        bool result = validator.IsUrlInScope("https://example.com/private/page.html", config);
        Assert.False(result);
    }

    [Fact]
    public void IsUrlInScope_ExcludedPattern_ReturnsFalse()
    {
        var validator = new CrawlBoundaryValidator();
        var config = MakeConfig(hosts: ["example.com"], excludePatterns: [".pdf"]);
        bool result = validator.IsUrlInScope("https://example.com/docs/file.pdf", config);
        Assert.False(result);
    }

    [Fact]
    public void IsUrlInScope_NonHttpScheme_ReturnsFalse()
    {
        var validator = new CrawlBoundaryValidator();
        var config = MakeConfig(hosts: ["example.com"]);
        bool result = validator.IsUrlInScope("ftp://example.com/file.txt", config);
        Assert.False(result);
    }

    [Fact]
    public void IsUrlInScope_UrlWithCredentials_ReturnsFalse()
    {
        var validator = new CrawlBoundaryValidator();
        var config = MakeConfig(hosts: ["example.com"]);
        bool result = validator.IsUrlInScope("https://user:pass@example.com/page", config);
        Assert.False(result);
    }

    // --- ValidateHostAsync ---

    [Fact]
    public async Task ValidateHostAsync_HostNotInAllowedList_ReturnsFalse()
    {
        var validator = new CrawlBoundaryValidator();
        var (allowed, reason) = await validator.ValidateHostAsync("other.com", ["example.com"]);
        Assert.False(allowed);
        Assert.NotNull(reason);
    }

    [Fact]
    public async Task ValidateHostAsync_LoopbackHost_ReturnsFalseWithLoopbackReason()
    {
        var validator = new CrawlBoundaryValidator();
        // localhost resolves to 127.0.0.1 which is loopback — always blocked
        var (allowed, reason) = await validator.ValidateHostAsync("localhost", ["localhost"]);
        Assert.False(allowed);
        Assert.NotNull(reason);
        Assert.Contains("loopback", reason, StringComparison.OrdinalIgnoreCase);
    }
}
