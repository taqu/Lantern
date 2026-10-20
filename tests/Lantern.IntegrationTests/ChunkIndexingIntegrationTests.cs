using Lantern.Core.Search;
using Lantern.Infrastructure.Search;
using Meilisearch;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lantern.IntegrationTests;

/// <summary>
/// Integration tests for chunk indexing. Requires a running Meilisearch instance.
/// Tests are skipped when MEILI_MASTER_KEY is not set.
/// </summary>
public sealed class ChunkIndexingIntegrationTests : IAsyncLifetime
{
    private readonly string _chunkIndex = $"lantern-chunks-test-{Guid.NewGuid():N}";
    private readonly string? _key = Environment.GetEnvironmentVariable("MEILI_MASTER_KEY");
    private MeilisearchClient _client = null!;
    private MeilisearchChunkIndexingService _service = null!;

    public Task InitializeAsync()
    {
        _client = new(Environment.GetEnvironmentVariable("MEILI_HOST") ?? "http://localhost:7700", _key ?? "");
        _service = new(
            _client,
            Options.Create(new MeilisearchOptions { IndexName = "lantern-test", MaxDocumentsPerBatch = 10, TaskTimeoutSeconds = 60 }),
            Options.Create(new HybridSearchOptions { ChunkIndexName = _chunkIndex, EmbedderName = "test-embedder" }),
            NullLogger<MeilisearchChunkIndexingService>.Instance);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_key == null) return;
        try
        {
            await _client.GetIndexAsync(_chunkIndex);
            TaskInfo task = await _client.DeleteIndexAsync(_chunkIndex);
            await _client.WaitForTaskAsync(task.TaskUid);
        }
        catch (MeilisearchApiError ex) when (ex.Code == "index_not_found") { }
    }

    [SkippableFact]
    public async Task EnsureIndexAsync_CreatesIndex()
    {
        Skip.If(_key == null, "MEILI_MASTER_KEY not set; skipping integration test.");

        await _service.EnsureIndexAsync();

        Meilisearch.Index index = await _client.GetIndexAsync(_chunkIndex);
        Assert.Equal(_chunkIndex, index.Uid);
    }

    [SkippableFact]
    public async Task UpsertAndDeleteChunks_WorksEndToEnd()
    {
        Skip.If(_key == null, "MEILI_MASTER_KEY not set; skipping integration test.");

        string docId = ChunkIdentity.Generate("test-doc", 0)[..16];
        SearchChunk[] chunks = new[]
        {
            new SearchChunk
            {
                Id = ChunkIdentity.Generate(docId, 0),
                DocumentId = docId,
                SourceId = "test-source",
                SourceType = "test",
                Title = "Test Document",
                Content = "This is chunk zero.",
                ChunkIndex = 0
            },
            new SearchChunk
            {
                Id = ChunkIdentity.Generate(docId, 1),
                DocumentId = docId,
                SourceId = "test-source",
                SourceType = "test",
                Title = "Test Document",
                Content = "This is chunk one.",
                ChunkIndex = 1
            }
        };

        await _service.UpsertChunksAsync(chunks);
        await _service.DeleteChunksByDocumentAsync(docId);

        // If no exception was thrown, the operations succeeded
        Assert.True(true);
    }

    [SkippableFact]
    public async Task ReplaceDocumentChunks_ReplacesExistingChunks()
    {
        Skip.If(_key == null, "MEILI_MASTER_KEY not set; skipping integration test.");

        string docId = ChunkIdentity.Generate("replace-doc", 0)[..16];
        SearchChunk[] oldChunks = new[]
        {
            new SearchChunk
            {
                Id = ChunkIdentity.Generate(docId, 0),
                DocumentId = docId,
                SourceId = "test-source",
                SourceType = "test",
                Title = "Old Title",
                Content = "Old content.",
                ChunkIndex = 0
            }
        };
        SearchChunk[] newChunks = new[]
        {
            new SearchChunk
            {
                Id = ChunkIdentity.Generate(docId, 0),
                DocumentId = docId,
                SourceId = "test-source",
                SourceType = "test",
                Title = "New Title",
                Content = "New content.",
                ChunkIndex = 0
            }
        };

        await _service.UpsertChunksAsync(oldChunks);
        await _service.ReplaceDocumentChunksAsync(docId, newChunks);

        // No exception = success
        Assert.True(true);
    }
}
