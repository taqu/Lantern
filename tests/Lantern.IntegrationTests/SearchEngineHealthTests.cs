using System.Net;
using Lantern.Infrastructure.Search;
using Meilisearch;

namespace Lantern.IntegrationTests;

public sealed class SearchEngineHealthTests
{
    [Fact]
    public async Task HealthyEmptyIndexIsDistinctFromUnavailableAndResponsesAreCached()
    {
        using HealthHandler handler = new HealthHandler();
        using HttpClient http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:7700") };
        MeilisearchClient client = new MeilisearchClient(http, "");
        MeilisearchHealthService service = new MeilisearchHealthService(new StubClientFactory(client, client), new MeilisearchOptions());
        Core.Administration.SearchEngineStatus status = await service.GetAsync();
        Assert.True(status.Available);
        Assert.Equal(0L, status.DocumentCount);
        Assert.Equal(status, await service.GetAsync());
        Assert.Equal(2, handler.Requests);
        await service.GetAsync(true);
        Assert.Equal(4, handler.Requests);
    }

    [Fact]
    public async Task MissingIndexDoesNotCreateOne()
    {
        using HttpClient http = new HttpClient(new HealthHandler { MissingIndex = true }) { BaseAddress = new Uri("http://localhost:7700") };
        MeilisearchClient client = new MeilisearchClient(http, "");
        MeilisearchHealthService service = new MeilisearchHealthService(new StubClientFactory(client, client), new MeilisearchOptions());
        Core.Administration.SearchEngineStatus status = await service.GetAsync();
        Assert.True(status.Available);
        Assert.Null(status.DocumentCount);
        Assert.Contains("does not exist", status.Message);
    }

    [Fact]
    public async Task ConnectionFailureReturnsSafeStatus()
    {
        using HttpClient http = new HttpClient(new HealthHandler { Offline = true }) { BaseAddress = new Uri("http://localhost:7700") };
        MeilisearchClient client = new MeilisearchClient(http, "");
        MeilisearchHealthService service = new MeilisearchHealthService(new StubClientFactory(client, client), new MeilisearchOptions());
        Core.Administration.SearchEngineStatus status = await service.GetAsync();
        Assert.False(status.Available);
        Assert.Null(status.DocumentCount);
        Assert.DoesNotContain("sensitive", status.Message);
    }

    [Fact]
    public async Task CallerCancellationIsNotConvertedIntoOfflineStatus()
    {
        using HttpClient http = new HttpClient(new HealthHandler()) { BaseAddress = new Uri("http://localhost:7700") };
        MeilisearchClient client = new MeilisearchClient(http, "");
        MeilisearchHealthService service = new MeilisearchHealthService(new StubClientFactory(client, client), new MeilisearchOptions());
        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAsync(cancellationToken: cts.Token));
    }

    [Fact]
    public async Task StatsPermissionDeniedDoesNotMarkEngineUnavailable()
    {
        // Search client responds to /health normally; stats client returns 403.
        using HttpClient searchHttp = new HttpClient(new HealthHandler { StatsOnly = false }) { BaseAddress = new Uri("http://localhost:7700") };
        using HttpClient statsHttp = new HttpClient(new HealthHandler { StatsDenied = true }) { BaseAddress = new Uri("http://localhost:7700") };
        MeilisearchClient searchClient = new MeilisearchClient(searchHttp, "");
        MeilisearchClient statsClient = new MeilisearchClient(statsHttp, "");
        MeilisearchHealthService service = new MeilisearchHealthService(new StubClientFactory(searchClient, statsClient), new MeilisearchOptions());
        Core.Administration.SearchEngineStatus status = await service.GetAsync();
        Assert.True(status.Available);
        Assert.Null(status.DocumentCount);
        Assert.Contains("statistics unavailable", status.Message);
    }

    private sealed class StubClientFactory(MeilisearchClient searchClient, MeilisearchClient indexingClient) : IMeilisearchClientFactory
    {
        public Task EnsureClientsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public MeilisearchClient GetSearchClient() => searchClient;
        public MeilisearchClient GetIndexingClient() => indexingClient;
        public MeilisearchClient GetAdminClient() => indexingClient;
    }

    private sealed class HealthHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public bool MissingIndex { get; init; }
        public bool Offline { get; init; }
        public bool StatsDenied { get; init; }
        public bool StatsOnly { get; init; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Assert.Equal(HttpMethod.Get, request.Method);
            if(Offline) throw new HttpRequestException("sensitive internal connection detail");
            bool health = request.RequestUri!.AbsolutePath == "/health";
            if(health) {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = new StringContent("""{"status":"available"}""", System.Text.Encoding.UTF8, "application/json")
                });
            }
            if(StatsDenied) {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) {
                    Content = new StringContent("""{"message":"Forbidden","code":"invalid_api_key","type":"auth","link":""}""", System.Text.Encoding.UTF8, "application/json")
                });
            }
            HttpStatusCode statsStatus = MissingIndex ? HttpStatusCode.NotFound : HttpStatusCode.OK;
            string statsBody = MissingIndex
                ? """{"message":"Index missing","code":"index_not_found","type":"invalid_request","link":"https://docs.meilisearch.com/errors#index_not_found"}"""
                : """{"numberOfDocuments":0,"isIndexing":false,"fieldDistribution":{}}""";
            return Task.FromResult(new HttpResponseMessage(statsStatus) {
                Content = new StringContent(statsBody, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
