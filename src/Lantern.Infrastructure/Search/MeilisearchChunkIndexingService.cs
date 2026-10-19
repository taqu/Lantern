using Lantern.Core.Search;
using Meilisearch;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lantern.Infrastructure.Search;

public sealed class MeilisearchChunkIndexingService : IChunkIndexingService
{
    private readonly MeilisearchClient _client;
    private readonly MeilisearchOptions _documentOptions;
    private readonly HybridSearchOptions _hybridOptions;
    private readonly ILogger<MeilisearchChunkIndexingService> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public MeilisearchChunkIndexingService(
        MeilisearchClient client,
        IOptions<MeilisearchOptions> documentOptions,
        IOptions<HybridSearchOptions> hybridOptions,
        ILogger<MeilisearchChunkIndexingService> logger)
    {
        _client = client;
        _documentOptions = documentOptions.Value;
        _hybridOptions = hybridOptions.Value;
        _logger = logger;
    }

    public async Task EnsureIndexAsync(CancellationToken cancellationToken = default)
    {
        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            await ConfigureAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task ConfigureAsync(CancellationToken cancellationToken)
    {
        string indexName = _hybridOptions.ChunkIndexName;
        _logger.LogInformation("Configuring chunk index '{IndexName}'", indexName);

        var task = await _client.CreateIndexAsync(indexName, "id");
        await WaitAsync(task.TaskUid, cancellationToken);

        var index = _client.Index(indexName);

        var settingsTask = await index.UpdateSettingsAsync(new Settings
        {
            SearchableAttributes = ["title", "sectionTitle", "content"],
            FilterableAttributes = ["documentId", "sourceId", "sourceType", "extension"],
            DisplayedAttributes = ["*"]
        });
        await WaitAsync(settingsTask.TaskUid, cancellationToken);
        _logger.LogInformation("Chunk index '{IndexName}' configured", indexName);
    }

    public async Task UpsertChunksAsync(IEnumerable<SearchChunk> chunks, CancellationToken cancellationToken = default)
    {
        await EnsureIndexAsync(cancellationToken);
        var index = _client.Index(_hybridOptions.ChunkIndexName);
        var batch = chunks.ToList();
        if (batch.Count == 0) return;

        var task = await index.AddDocumentsAsync(batch, primaryKey: "id");
        await WaitAsync(task.TaskUid, cancellationToken);
        _logger.LogDebug("Upserted {Count} chunks", batch.Count);
    }

    public async Task DeleteChunksByDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        await EnsureIndexAsync(cancellationToken);
        var index = _client.Index(_hybridOptions.ChunkIndexName);
        var query = new Meilisearch.QueryParameters.DeleteDocumentsQuery { Filter = $"documentId = \"{documentId}\"" };
        var task = await index.DeleteDocumentsAsync(query, cancellationToken);
        await WaitAsync(task.TaskUid, cancellationToken);
    }

    public async Task ReplaceDocumentChunksAsync(string documentId, IEnumerable<SearchChunk> newChunks, CancellationToken cancellationToken = default)
    {
        await EnsureIndexAsync(cancellationToken);
        var chunkList = newChunks.ToList();

        // Delete all existing chunks for the document, then upsert new ones.
        // Not atomic but reduces stale chunk window.
        await DeleteChunksByDocumentAsync(documentId, cancellationToken);
        if (chunkList.Count > 0)
            await UpsertChunksAsync(chunkList, cancellationToken);
    }

    public async Task DeleteChunkAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        await EnsureIndexAsync(cancellationToken);
        var index = _client.Index(_hybridOptions.ChunkIndexName);
        var task = await index.DeleteOneDocumentAsync(chunkId);
        await WaitAsync(task.TaskUid, cancellationToken);
    }

    private async Task WaitAsync(int taskUid, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_documentOptions.TaskTimeoutSeconds));
        TaskResource result;
        try
        {
            result = await _client.WaitForTaskAsync(taskUid,
                timeoutMs: _documentOptions.TaskTimeoutSeconds * 1000d,
                intervalMs: 100, cancellationToken: cts.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Meilisearch task {taskUid} timed out after {_documentOptions.TaskTimeoutSeconds}s.", ex);
        }
        if (result.Status != TaskInfoStatus.Succeeded)
            throw new InvalidOperationException($"Meilisearch task {taskUid}: {result.Status}; {System.Text.Json.JsonSerializer.Serialize(result.Error)}");
    }
}
