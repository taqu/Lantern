using System.Net;
using Lantern.Infrastructure.Search;
using Meilisearch;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lantern.IntegrationTests;

public sealed class TaskCompletionTests
{
    [Fact]
    public async Task UnfinishedTaskTimesOutWithOperationAndUid()
    {
        using var http = new HttpClient(new ProcessingTaskHandler()) { BaseAddress = new Uri("http://localhost:7700") };
        var service = new MeilisearchIndexingService(new MeilisearchClient(http, ""),
            new MeilisearchOptions { TaskTimeoutSeconds = 1 }, NullLogger<MeilisearchIndexingService>.Instance);
        TimeoutException error = await Assert.ThrowsAsync<TimeoutException>(() => service.WaitAsync(7, "upsert"));
        Assert.Contains("7", error.Message);
        Assert.Contains("upsert", error.Message);
    }

    [Fact]
    public async Task WaitingTaskHonorsCallerCancellation()
    {
        using var http = new HttpClient(new ProcessingTaskHandler()) { BaseAddress = new Uri("http://localhost:7700") };
        var service = new MeilisearchIndexingService(new MeilisearchClient(http, ""),
            new MeilisearchOptions(), NullLogger<MeilisearchIndexingService>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.WaitAsync(7, "upsert", cts.Token));
    }

    private sealed class ProcessingTaskHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"uid":7,"status":"processing","type":"documentAdditionOrUpdate","enqueuedAt":"2026-01-01T00:00:00Z"}""", System.Text.Encoding.UTF8, "application/json")
            });
    }
}
