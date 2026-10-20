using Lantern.Crawling.Web;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lantern.IntegrationTests.Web;

public class RobotsPolicyTests
{
    private static HttpClient MakeClient(string url, string content, System.Net.HttpStatusCode statusCode = System.Net.HttpStatusCode.OK)
    {
        var handler = new FakeHttpMessageHandler();
        handler.Register(url, statusCode, content);
        return new HttpClient(handler);
    }

    private static HttpClient MakeClientWithRobots(string origin, string robotsContent, System.Net.HttpStatusCode statusCode = System.Net.HttpStatusCode.OK)
        => MakeClient($"{origin}/robots.txt", robotsContent, statusCode);

    [Fact]
    public async Task IsAllowedAsync_BasicDisallow_BlocksPath()
    {
        const string robots = """
            User-agent: *
            Disallow: /private/
            """;
        var policy = new RobotsPolicy("LanternCrawler", NullLogger.Instance);
        using var client = MakeClientWithRobots("https://example.com", robots);

        bool blocked = await policy.IsAllowedAsync("https://example.com/private/page.html", client, CancellationToken.None);
        bool allowed = await policy.IsAllowedAsync("https://example.com/public/page.html", client, CancellationToken.None);

        Assert.False(blocked);
        Assert.True(allowed);
    }

    [Fact]
    public async Task IsAllowedAsync_AllowOverridesDisallow_MoreSpecificWins()
    {
        const string robots = """
            User-agent: *
            Disallow: /docs/
            Allow: /docs/public/
            """;
        var policy = new RobotsPolicy("LanternCrawler", NullLogger.Instance);
        using var client = MakeClientWithRobots("https://example.com", robots);

        bool secretBlocked = await policy.IsAllowedAsync("https://example.com/docs/secret.html", client, CancellationToken.None);
        bool publicAllowed = await policy.IsAllowedAsync("https://example.com/docs/public/index.html", client, CancellationToken.None);

        Assert.False(secretBlocked);
        Assert.True(publicAllowed);
    }

    [Fact]
    public async Task IsAllowedAsync_LanternCrawlerSpecificRules_Applied()
    {
        const string robots = """
            User-agent: LanternCrawler
            Disallow: /internal/
            """;
        var policy = new RobotsPolicy("LanternCrawler", NullLogger.Instance);
        using var client = MakeClientWithRobots("https://example.com", robots);

        bool blocked = await policy.IsAllowedAsync("https://example.com/internal/page.html", client, CancellationToken.None);

        Assert.False(blocked);
    }

    [Fact]
    public async Task IsAllowedAsync_RobotsReturns404_AllPathsAllowed()
    {
        var policy = new RobotsPolicy("LanternCrawler", NullLogger.Instance);
        using var client = MakeClientWithRobots("https://example.com", "", System.Net.HttpStatusCode.NotFound);

        bool allowed = await policy.IsAllowedAsync("https://example.com/anything/page.html", client, CancellationToken.None);

        Assert.True(allowed);
    }

    [Fact]
    public async Task IsAllowedAsync_RobotsFetchThrows_AllPathsAllowed()
    {
        var handler = new ThrowingHttpMessageHandler();
        using var client = new HttpClient(handler);
        var policy = new RobotsPolicy("LanternCrawler", NullLogger.Instance);

        bool allowed = await policy.IsAllowedAsync("https://example.com/page.html", client, CancellationToken.None);

        Assert.True(allowed);
    }

    [Fact]
    public async Task IsAllowedAsync_EmptyRobotsTxt_AllPathsAllowed()
    {
        var policy = new RobotsPolicy("LanternCrawler", NullLogger.Instance);
        using var client = MakeClientWithRobots("https://example.com", "");

        bool allowed = await policy.IsAllowedAsync("https://example.com/any/path.html", client, CancellationToken.None);

        Assert.True(allowed);
    }

    // --- Helpers ---

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (System.Net.HttpStatusCode Status, string Content)> _responses
            = new(StringComparer.OrdinalIgnoreCase);

        public void Register(string url, System.Net.HttpStatusCode status, string content)
            => _responses[url] = (status, content);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri?.AbsoluteUri ?? "";
            if (_responses.TryGetValue(url, out var entry))
            {
                var response = new HttpResponseMessage(entry.Status)
                {
                    Content = new StringContent(entry.Content)
                };
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
            {
                Content = new StringContent("")
            });
        }
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Simulated network failure");
    }
}
