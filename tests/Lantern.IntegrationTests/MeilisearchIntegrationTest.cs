using Meilisearch;

namespace Lantern.IntegrationTests;

/// <summary>
/// Integration tests for Meilisearch connectivity.
///
/// These tests require a running Meilisearch instance.
/// Set MEILI_MASTER_KEY to enable; tests are skipped when the variable is absent.
/// Optionally set MEILI_HOST (default: http://localhost:7700).
/// </summary>
public sealed class MeilisearchIntegrationTest : IAsyncLifetime
{
    private const string IndexUid = "integration-test";
    private const double TimeoutMs = 30_000;

    private readonly string? _masterKey;
    private readonly string _host;
    private MeilisearchClient? _client;

    public MeilisearchIntegrationTest()
    {
        _masterKey = Environment.GetEnvironmentVariable("MEILI_MASTER_KEY");
        _host = Environment.GetEnvironmentVariable("MEILI_HOST") ?? "http://localhost:7700";
    }

    public Task InitializeAsync()
    {
        if (_masterKey != null)
            _client = new MeilisearchClient(_host, _masterKey);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_client == null) return;
        try
        {
            var task = await _client.DeleteIndexAsync(IndexUid);
            await _client.WaitForTaskAsync(task.TaskUid, timeoutMs: TimeoutMs);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [SkippableFact]
    public async Task Meilisearch_IsHealthy()
    {
        Skip.If(_masterKey == null, "MEILI_MASTER_KEY not set — skipping Meilisearch integration test.");

        var health = await _client!.HealthAsync();
        Assert.Equal("available", health.Status);
    }

    [SkippableFact]
    public async Task IndexDocumentsAndSearch_ReturnsExpectedResults()
    {
        Skip.If(_masterKey == null, "MEILI_MASTER_KEY not set — skipping Meilisearch integration test.");

        // Clean up any previous run
        await EnsureIndexDeletedAsync();

        // Create index
        var createTask = await _client!.CreateIndexAsync(IndexUid, "id");
        var createResult = await _client.WaitForTaskAsync(createTask.TaskUid, timeoutMs: TimeoutMs);
        Assert.Equal(TaskInfoStatus.Succeeded, createResult.Status);

        var index = _client.Index(IndexUid);

        // Index documents
        var documents = new[]
        {
            new TestDocument { Id = 1, Title = "Lantern Introduction",  Content = "Lantern is a lightweight full-text search platform." },
            new TestDocument { Id = 2, Title = "Document Extraction",   Content = "PDF and Office documents can be indexed." },
            new TestDocument { Id = 3, Title = "Web Crawling",          Content = "Website content can be discovered and searched." },
        };

        var addTask = await index.AddDocumentsAsync(documents);
        var addResult = await _client.WaitForTaskAsync(addTask.TaskUid, timeoutMs: TimeoutMs);
        Assert.Equal(TaskInfoStatus.Succeeded, addResult.Status);

        // Search
        var results = await index.SearchAsync<TestDocument>("lightweight");

        Assert.NotNull(results);
        Assert.NotEmpty(results.Hits);
        Assert.Contains(results.Hits, h => h.Id == 1);
    }

    [SkippableFact]
    public async Task Search_ReturnsNoResults_ForUnknownTerm()
    {
        Skip.If(_masterKey == null, "MEILI_MASTER_KEY not set — skipping Meilisearch integration test.");

        await EnsureIndexDeletedAsync();

        var createTask = await _client!.CreateIndexAsync(IndexUid, "id");
        await _client.WaitForTaskAsync(createTask.TaskUid, timeoutMs: TimeoutMs);

        var index = _client.Index(IndexUid);

        var addTask = await index.AddDocumentsAsync(new[]
        {
            new TestDocument { Id = 1, Title = "Only Document", Content = "Hello world." }
        });
        await _client.WaitForTaskAsync(addTask.TaskUid, timeoutMs: TimeoutMs);

        var results = await index.SearchAsync<TestDocument>("zzzznonexistentterm");

        Assert.NotNull(results);
        Assert.Empty(results.Hits);
    }

    [SkippableFact]
    public async Task DeleteIndex_Succeeds()
    {
        Skip.If(_masterKey == null, "MEILI_MASTER_KEY not set — skipping Meilisearch integration test.");

        // Create then delete
        var createTask = await _client!.CreateIndexAsync(IndexUid, "id");
        await _client.WaitForTaskAsync(createTask.TaskUid, timeoutMs: TimeoutMs);

        var deleteTask = await _client.DeleteIndexAsync(IndexUid);
        var deleteResult = await _client.WaitForTaskAsync(deleteTask.TaskUid, timeoutMs: TimeoutMs);

        Assert.Equal(TaskInfoStatus.Succeeded, deleteResult.Status);
    }

    private async Task EnsureIndexDeletedAsync()
    {
        if (_client == null) return;
        try
        {
            var indexes = await _client.GetAllIndexesAsync();
            if (indexes.Results.Any(i => i.Uid == IndexUid))
            {
                var task = await _client.DeleteIndexAsync(IndexUid);
                await _client.WaitForTaskAsync(task.TaskUid, timeoutMs: TimeoutMs);
            }
        }
        catch
        {
            // Ignore — index may not exist.
        }
    }

    private sealed class TestDocument
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }
}
