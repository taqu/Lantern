using System.Net;
using Lantern.Infrastructure.Search;
using Meilisearch;

namespace Lantern.IntegrationTests;

public sealed class SearchEngineHealthTests
{
    [Fact]
    public async Task HealthyEmptyIndexIsDistinctFromUnavailableAndResponsesAreCached()
    {
        using var handler = new HealthHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:7700") };
        var service = new MeilisearchHealthService(new MeilisearchClient(http, ""), new MeilisearchOptions());
        var status = await service.GetAsync();
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
        using var http = new HttpClient(new HealthHandler { MissingIndex = true }) { BaseAddress = new Uri("http://localhost:7700") };
        var client = new MeilisearchClient(http, "");
        var service = new MeilisearchHealthService(client, new MeilisearchOptions());
        var status = await service.GetAsync();
        Assert.True(status.Available);
        Assert.Null(status.DocumentCount);
        Assert.Contains("does not exist", status.Message);
    }

    [Fact]
    public async Task ConnectionFailureReturnsSafeStatus()
    {
        using var http = new HttpClient(new HealthHandler { Offline = true }) { BaseAddress = new Uri("http://localhost:7700") };
        var service = new MeilisearchHealthService(new MeilisearchClient(http, ""), new MeilisearchOptions());
        var status = await service.GetAsync();
        Assert.False(status.Available);
        Assert.Null(status.DocumentCount);
        Assert.DoesNotContain("sensitive", status.Message);
    }

    [Fact]
    public async Task CallerCancellationIsNotConvertedIntoOfflineStatus()
    {
        using var http = new HttpClient(new HealthHandler()) { BaseAddress = new Uri("http://localhost:7700") };
        var service = new MeilisearchHealthService(new MeilisearchClient(http, ""), new MeilisearchOptions());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAsync(cancellationToken: cts.Token));
    }

    private sealed class HealthHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public bool MissingIndex { get; init; }
        public bool Offline { get; init; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Assert.Equal(HttpMethod.Get, request.Method);
            if (Offline) throw new HttpRequestException("sensitive internal connection detail");
            var health = request.RequestUri!.AbsolutePath == "/health";
            var response = new HttpResponseMessage(!health && MissingIndex ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            {
                Content = new StringContent(health ? """{"status":"available"}""" : MissingIndex
                    ? """{"message":"Index missing","code":"index_not_found","type":"invalid_request","link":"https://docs.meilisearch.com/errors#index_not_found"}"""
                    : """{"numberOfDocuments":0,"isIndexing":false,"fieldDistribution":{}}""", System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
