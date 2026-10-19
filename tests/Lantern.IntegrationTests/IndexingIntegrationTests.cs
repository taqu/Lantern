using Lantern.Core.Search;
using Lantern.Infrastructure.Search;
using Meilisearch;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lantern.IntegrationTests;

public sealed class IndexingIntegrationTests : IAsyncLifetime
{
    private readonly string _name = $"lantern-phase2-test-{Guid.NewGuid():N}";
    private readonly string? _key = Environment.GetEnvironmentVariable("MEILI_MASTER_KEY");
    private MeilisearchClient _client = null!;
    private MeilisearchIndexingService _service = null!;

    public Task InitializeAsync()
    {
        _client = new(Environment.GetEnvironmentVariable("MEILI_HOST") ?? "http://localhost:7700", _key ?? "");
        _service = new(_client, new MeilisearchOptions { IndexName = _name, MaxDocumentsPerBatch = 2 }, NullLogger<MeilisearchIndexingService>.Instance);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_key == null) return;
        try
        {
            await _client.GetIndexAsync(_name);
            await _service.WaitAsync((await _client.DeleteIndexAsync(_name)).TaskUid, "test cleanup");
        }
        catch (MeilisearchApiError ex) when (ex.Code == "index_not_found") { }
    }

    private static SearchDocument Doc(int i, string content = "日本語 lantern foundation") => new()
    {
        SourceId = "test", SourceType = "web", Uri = $"https://example.com/{i}", Title = $"Document {i}",
        Content = content, MimeType = "text/html", Size = 100
    };

    [SkippableFact]
    public async Task ConfigureUpsertSearchUpdateDeleteAndRebuild()
    {
        Skip.If(_key == null, "Set MEILI_MASTER_KEY to run against a real Meilisearch instance.");
        await _service.EnsureIndexAsync();
        await _service.EnsureIndexAsync();
        var index = await _client.GetIndexAsync(_name);
        Assert.Equal("id", index.PrimaryKey);
        var settings = await index.GetSettingsAsync();
        Assert.Equal(new[] { "title", "content" }, settings.SearchableAttributes);
        Assert.Equal(new[] { "indexedAt", "modifiedAt", "size" }, settings.SortableAttributes!.Order());
        Assert.Equal(4, settings.FilterableAttributes!.Count());
        await _service.UpsertAsync(Doc(0));
        var reinitialize = new MeilisearchIndexingService(_client, new MeilisearchOptions { IndexName = _name }, NullLogger<MeilisearchIndexingService>.Instance);
        await reinitialize.EnsureIndexAsync();
        Assert.Equal(1L, (await index.GetStatsAsync()).NumberOfDocuments);
        await _service.UpsertBatchAsync(Enumerable.Range(1, 5).Select(i => Doc(i)));
        await _service.UpsertAsync(Doc(0, "replacement content"));
        Assert.Equal(6L, (await index.GetStatsAsync()).NumberOfDocuments);
        var health = await new MeilisearchHealthService(_client, new MeilisearchOptions { IndexName = _name }).GetAsync(true);
        Assert.True(health.Available);
        Assert.Equal(6L, health.DocumentCount);
        Assert.Equal(_name, health.IndexName);
        var found = await index.SearchAsync<SearchDocument>("replacement");
        var hit = Assert.Single(found.Hits);
        Assert.Equal("test", hit.SourceId);
        Assert.Equal("https://example.com/0", hit.Uri);
        Assert.Equal("web", hit.SourceType);
        Assert.Equal("text/html", hit.MimeType);
        var search = new MeilisearchSearchService(new MeilisearchOptions
        { Url = Environment.GetEnvironmentVariable("MEILI_HOST") ?? "http://localhost:7700", IndexName = _name }, _key,
            NullLogger<MeilisearchSearchService>.Instance, _service);
        var page = await search.SearchPageAsync("replacement", 1);
        Assert.Equal(hit.Uri, Assert.Single(page.Items).Url);
        Assert.Equal("test", page.Items[0].SourceId);
        Assert.Equal("text/html", page.Items[0].MimeType);
        await _service.DeleteAsync(hit.Id);
        await _service.DeleteAsync(hit.Id);
        await _service.DeleteBatchAsync(new[] { DocumentNormalizer.Normalize(Doc(1)).Id, DocumentNormalizer.Normalize(Doc(2)).Id });
        Assert.Equal(3L, (await index.GetStatsAsync()).NumberOfDocuments);
        await _service.RebuildAsync(new[] { Doc(10, "rebuilt corpus") });
        Assert.Equal(1L, (await index.GetStatsAsync()).NumberOfDocuments);
        Assert.Single((await index.SearchAsync<SearchDocument>("rebuilt")).Hits);
        // Force validation failure after at least one bounded batch has reached the temporary index.
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RebuildAsync(new[] { Doc(20), Doc(21), Doc(22), new SearchDocument { Title = "", Content = "" } }));
        Assert.Single((await index.SearchAsync<SearchDocument>("rebuilt")).Hits);
        await _service.UpsertAsync(Doc(11));
        Assert.Equal(2L, (await index.GetStatsAsync()).NumberOfDocuments);
        var indexes = await _client.GetAllIndexesAsync();
        Assert.DoesNotContain(indexes.Results, i => i.Uid.StartsWith(_name + "-rebuild-", StringComparison.Ordinal));
    }

    [SkippableFact]
    public async Task FailedTaskIsReportedWithTaskIdentity()
    {
        Skip.If(_key == null, "Set MEILI_MASTER_KEY to run against a real Meilisearch instance.");
        await _service.EnsureIndexAsync();
        var task = await _client.Index(_name).AddDocumentsAsync(new[] { new { id = "invalid id with spaces" } });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.WaitAsync(task.TaskUid, "invalid document"));
        Assert.Contains(task.TaskUid.ToString(), error.Message);
        Assert.Contains("invalid_document_id", error.Message);
    }

    [SkippableFact]
    public async Task CancellationDoesNotReportSuccess()
    {
        Skip.If(_key == null, "Set MEILI_MASTER_KEY to run against a real Meilisearch instance.");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.UpsertAsync(Doc(0), cts.Token));
    }
}
