using Lantern.Crawling.Web;

namespace Lantern.IntegrationTests.Web;

public class UrlNormalizerTests
{
    // --- TryNormalize ---

    [Fact]
    public void TryNormalize_RemovesFragment()
    {
        string? result = UrlNormalizer.TryNormalize("https://example.com/page#section");
        Assert.Equal("https://example.com/page", result);
    }

    [Fact]
    public void TryNormalize_NormalizesSchemeToLowercase()
    {
        string? result = UrlNormalizer.TryNormalize("HTTPS://EXAMPLE.COM/");
        Assert.NotNull(result);
        Assert.StartsWith("https://", result);
    }

    [Fact]
    public void TryNormalize_NormalizesHostToLowercase()
    {
        string? result = UrlNormalizer.TryNormalize("https://EXAMPLE.COM/path");
        Assert.NotNull(result);
        Assert.Contains("example.com", result);
    }

    [Fact]
    public void TryNormalize_RemovesDefaultPortHttp()
    {
        string? result = UrlNormalizer.TryNormalize("http://example.com:80/path");
        Assert.Equal("http://example.com/path", result);
    }

    [Fact]
    public void TryNormalize_RemovesDefaultPortHttps()
    {
        string? result = UrlNormalizer.TryNormalize("https://example.com:443/path");
        Assert.Equal("https://example.com/path", result);
    }

    [Fact]
    public void TryNormalize_PreservesNonDefaultPort()
    {
        string? result = UrlNormalizer.TryNormalize("https://example.com:8443/path");
        Assert.Equal("https://example.com:8443/path", result);
    }

    [Fact]
    public void TryNormalize_ResolvesRelativeUrlWithBase()
    {
        string? result = UrlNormalizer.TryNormalize("../other.html", "https://example.com/docs/page.html");
        Assert.Equal("https://example.com/other.html", result);
    }

    [Fact]
    public void TryNormalize_ResolvesRootRelativeUrlWithBase()
    {
        string? result = UrlNormalizer.TryNormalize("/about", "https://example.com/docs/");
        Assert.Equal("https://example.com/about", result);
    }

    [Fact]
    public void TryNormalize_ResolvesProtocolRelativeUrl()
    {
        string? result = UrlNormalizer.TryNormalize("//cdn.example.com/script.js", "https://example.com/");
        Assert.Equal("https://cdn.example.com/script.js", result);
    }

    [Fact]
    public void TryNormalize_PreservesQueryParameters()
    {
        string input = "https://example.com/search?q=test&lang=ja";
        string? result = UrlNormalizer.TryNormalize(input);
        Assert.NotNull(result);
        Assert.Contains("q=test", result);
        Assert.Contains("lang=ja", result);
    }

    [Fact]
    public void TryNormalize_PreservesPathCase()
    {
        string? result = UrlNormalizer.TryNormalize("https://example.com/Docs/Guide.html");
        Assert.NotNull(result);
        Assert.Contains("/Docs/Guide.html", result);
    }

    [Fact]
    public void TryNormalize_RejectsUrlWithCredentials()
    {
        string? result = UrlNormalizer.TryNormalize("https://user:pass@example.com/");
        Assert.Null(result);
    }

    [Fact]
    public void TryNormalize_RejectsNonHttpScheme()
    {
        string? result = UrlNormalizer.TryNormalize("ftp://example.com/");
        Assert.Null(result);
    }

    [Fact]
    public void TryNormalize_NullInputReturnsNull()
    {
        string? result = UrlNormalizer.TryNormalize(null);
        Assert.Null(result);
    }

    [Fact]
    public void TryNormalize_EmptyStringReturnsNull()
    {
        string? result = UrlNormalizer.TryNormalize("");
        Assert.Null(result);
    }

    // --- TryGetOrigin ---

    [Fact]
    public void TryGetOrigin_WithNonDefaultPort_IncludesPort()
    {
        bool ok = UrlNormalizer.TryGetOrigin("https://example.com:8080/path", out string origin);
        Assert.True(ok);
        Assert.Equal("https://example.com:8080", origin);
    }

    [Fact]
    public void TryGetOrigin_WithDefaultPort_OmitsPort()
    {
        bool ok = UrlNormalizer.TryGetOrigin("https://example.com/path", out string origin);
        Assert.True(ok);
        Assert.Equal("https://example.com", origin);
    }

    // --- GetHost ---

    [Fact]
    public void GetHost_ReturnsLowercaseHost()
    {
        string? host = UrlNormalizer.GetHost("https://EXAMPLE.COM/path");
        Assert.Equal("example.com", host);
    }
}
