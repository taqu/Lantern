using Lantern.Core.Search;
using Lantern.Infrastructure.Search;
using Meilisearch;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lantern.IntegrationTests;

/// <summary>
/// Integration tests for MeilisearchSearchService.
///
/// Requires a running Meilisearch instance.
/// Set MEILI_MASTER_KEY to enable; tests are skipped when the variable is absent.
/// Optionally set MEILI_HOST (default: http://localhost:7700).
/// </summary>
public sealed class SearchServiceIntegrationTest : IAsyncLifetime
{
    private const string IndexName = "searchservice-test";
    private const double TimeoutMs = 30_000;

    private readonly string? _masterKey;
    private readonly string _host;
    private MeilisearchClient? _rawClient;

    public SearchServiceIntegrationTest()
    {
        _masterKey = Environment.GetEnvironmentVariable("MEILI_MASTER_KEY");
        _host = Environment.GetEnvironmentVariable("MEILI_HOST") ?? "http://localhost:7700";
    }

    public Task InitializeAsync()
    {
        if (_masterKey != null)
            _rawClient = new MeilisearchClient(_host, _masterKey);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_rawClient == null) return;
        try
        {
            TaskInfo task = await _rawClient.DeleteIndexAsync(IndexName);
            await _rawClient.WaitForTaskAsync(task.TaskUid, timeoutMs: TimeoutMs);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private MeilisearchSearchService CreateService()
    {
        var options = new MeilisearchOptions
        {
            Url = _host,
            IndexName = IndexName
        };
        return new MeilisearchSearchService(options, _masterKey, NullLogger<MeilisearchSearchService>.Instance);
    }

    private async Task EnsureIndexReadyAsync(IEnumerable<object> documents)
    {
        // Drop any leftover index
        try
        {
            ResourceResults<IEnumerable<Meilisearch.Index>> indexes = await _rawClient!.GetAllIndexesAsync();
            if (indexes.Results.Any(i => i.Uid == IndexName))
            {
                TaskInfo del = await _rawClient.DeleteIndexAsync(IndexName);
                await _rawClient.WaitForTaskAsync(del.TaskUid, timeoutMs: TimeoutMs);
            }
        }
        catch { }

        // Create fresh
        TaskInfo create = await _rawClient!.CreateIndexAsync(IndexName, "id");
        await _rawClient.WaitForTaskAsync(create.TaskUid, timeoutMs: TimeoutMs);

        Meilisearch.Index index = _rawClient.Index(IndexName);
        TaskInfo add = await index.AddDocumentsAsync(documents);
        await _rawClient.WaitForTaskAsync(add.TaskUid, timeoutMs: TimeoutMs);
    }

    [SkippableFact]
    public async Task SearchPageAsync_ValidQuery_ReturnsResults()
    {
        Skip.If(_masterKey == null, "MEILI_MASTER_KEY not set — skipping integration test.");

        var docs = new[]
        {
            new { id = "s-001", title = "Lightweight Search", content = "Lantern is a lightweight full-text search platform.", url = (string?)null, sourcePath = (string?)null, documentType = "HTML" },
            new { id = "s-002", title = "Meilisearch Basics", content = "Meilisearch powers the search backend.", url = (string?)null, sourcePath = (string?)null, documentType = "HTML" },
        };
        await EnsureIndexReadyAsync(docs);

        MeilisearchSearchService service = CreateService();
        SearchPageResponse response = await service.SearchPageAsync("lightweight", 1);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Items);
        Assert.Contains(response.Items, i => i.Id == "s-001");
        Assert.Equal("lightweight", response.Query);
        Assert.Equal(1, response.Page);
    }

    [SkippableFact]
    public async Task SearchPageAsync_EmptyQuery_ReturnsEmptyResponse()
    {
        Skip.If(_masterKey == null, "MEILI_MASTER_KEY not set — skipping integration test.");

        MeilisearchSearchService service = CreateService();

        SearchPageResponse response = await service.SearchPageAsync("", 1);

        Assert.NotNull(response);
        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalHits);
    }

    [SkippableFact]
    public async Task SearchPageAsync_WhitespaceQuery_ReturnsEmptyResponse()
    {
        Skip.If(_masterKey == null, "MEILI_MASTER_KEY not set — skipping integration test.");

        MeilisearchSearchService service = CreateService();
        SearchPageResponse response = await service.SearchPageAsync("   ", 1);

        Assert.NotNull(response);
        Assert.Empty(response.Items);
    }

    [SkippableFact]
    public async Task SearchPageAsync_Pagination_SecondPageExists()
    {
        Skip.If(_masterKey == null, "MEILI_MASTER_KEY not set — skipping integration test.");

        // Insert 15 documents all containing "platform"
        var docs = Enumerable.Range(1, 15).Select(i => new
        {
            id = $"p-{i:D3}",
            title = $"Document {i}",
            content = $"This is document number {i} about the platform.",
            url = (string?)null,
            sourcePath = (string?)null,
            documentType = "TXT"
        }).ToArray();
        await EnsureIndexReadyAsync(docs);

        MeilisearchSearchService service = CreateService();

        SearchPageResponse page1 = await service.SearchPageAsync("platform", 1);
        SearchPageResponse page2 = await service.SearchPageAsync("platform", 2);

        Assert.Equal(1, page1.Page);
        Assert.Equal(2, page2.Page);
        Assert.True(page1.TotalHits >= 15, $"Expected at least 15 hits, got {page1.TotalHits}");
        Assert.NotEmpty(page2.Items);

        // Ensure page 1 and page 2 have different items
        var page1Ids = page1.Items.Select(i => i.Id).ToHashSet();
        Assert.True(page2.Items.Any(i => !page1Ids.Contains(i.Id)),
            "Page 2 should contain items not present on page 1.");
    }
}
