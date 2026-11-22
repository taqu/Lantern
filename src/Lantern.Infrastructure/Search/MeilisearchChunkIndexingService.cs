using System.Text.Json;
using Lantern.Core.Search;
using Meilisearch;
using Meilisearch.QueryParameters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lantern.Infrastructure.Search;

public sealed class MeilisearchChunkIndexingService : IChunkIndexingService
{
    private readonly IMeilisearchClientFactory _clientFactory;
    private MeilisearchClient _client = null!;
    private readonly MeilisearchOptions _documentOptions;
    private readonly HybridSearchOptions _hybridOptions;
    private readonly ILogger<MeilisearchChunkIndexingService> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public MeilisearchChunkIndexingService(
        IMeilisearchClientFactory clientFactory,
        IOptions<MeilisearchOptions> documentOptions,
        IOptions<HybridSearchOptions> hybridOptions,
        ILogger<MeilisearchChunkIndexingService> logger)
    {
        _clientFactory = clientFactory;
        _documentOptions = documentOptions.Value;
        _hybridOptions = hybridOptions.Value;
        _logger = logger;
    }

    public async Task EnsureIndexAsync(CancellationToken cancellationToken = default)
    {
        if(!_hybridOptions.Enabled) return;
        await _initLock.WaitAsync(cancellationToken);
        try {
            if(_initialized) return;
            await ConfigureAsync(cancellationToken);
            _initialized = true;
        } finally {
            _initLock.Release();
        }
    }

    private async Task EnsureCompatiblePrimaryKeyAsync(Meilisearch.Index index, string name, CancellationToken ct)
    {
        if(index.PrimaryKey == "id") return;
        if(!string.IsNullOrEmpty(index.PrimaryKey))
            throw new InvalidOperationException($"Chunk index '{name}' exists with incompatible primary key '{index.PrimaryKey}'.");

        // Primary key is unset in the metadata DTO. Re-fetch current metadata to confirm
        // the index still exists and get its latest state before attempting any writes.
        Meilisearch.Index current;
        try {
            current = await _client.GetIndexAsync(name, ct);
        } catch(MeilisearchApiError ex) when(ex.Code is "missing_authorization_header" or "invalid_api_key") {
            throw new MeilisearchAuthenticationException(ex);
        } catch(MeilisearchApiError ex) when(ex.Code == "index_not_found") {
            throw new InvalidOperationException(
                $"Chunk index '{name}' disappeared between creation and primary-key assignment. " +
                $"Meilisearch error code: {ex.Code}", ex);
        }

        if(current.PrimaryKey == "id") return;
        if(!string.IsNullOrEmpty(current.PrimaryKey))
            throw new InvalidOperationException(
                $"Chunk index '{name}' primary key was concurrently set to '{current.PrimaryKey}'; expected 'id'.");

        // Index exists with no primary key set. Check whether it is safe to assign one.
        // Use _client.Index() for a live SDK-managed reference, not the deserialized DTO.
        IndexStats stats;
        try {
            stats = await _client.Index(name).GetStatsAsync();
        } catch(MeilisearchApiError ex) when(ex.Code is "missing_authorization_header" or "invalid_api_key") {
            throw new MeilisearchAuthenticationException(ex);
        } catch(MeilisearchApiError ex) when(ex.Code == "index_not_found") {
            throw new InvalidOperationException(
                $"Chunk index '{name}' was confirmed to exist but returned index_not_found when fetching stats. " +
                $"Unexpected Meilisearch state. Code: {ex.Code}", ex);
        } catch(Exception ex) when(ex is not OperationCanceledException) {
            throw new InvalidOperationException(
                $"Chunk index '{name}' has no primary key set but fetching its statistics failed. " +
                "Cannot safely assign primary key 'id' without knowing whether the index contains documents. " +
                $"Underlying error ({ex.GetType().Name}): {ex.Message}", ex);
        }

        if(stats.NumberOfDocuments > 0)
            throw new InvalidOperationException(
                $"Chunk index '{name}' has an unset primary key and contains {stats.NumberOfDocuments} document(s). " +
                "Cannot safely assign primary key 'id'. Set the primary key manually in Meilisearch before crawling.");

        TaskInfo setPkTask = await _client.UpdateIndexAsync(name, "id", ct);
        TaskResource setPkResult = await WaitForTaskRawAsync(setPkTask.TaskUid, ct);
        if(setPkResult.Status != TaskInfoStatus.Succeeded)
            throw new InvalidOperationException(
                $"Chunk index '{name}': failed to set primary key: task {setPkTask.TaskUid} {setPkResult.Status}; {System.Text.Json.JsonSerializer.Serialize(setPkResult.Error)}");
        _logger.LogInformation("Assigned primary key 'id' on chunk index '{Name}' that had no primary key set", name);
    }

    private async Task ConfigureAsync(CancellationToken cancellationToken)
    {
        if(null == _client) {
            _client = _clientFactory.GetIndexingClient();
        }
        string indexName = _hybridOptions.ChunkIndexName;
        _logger.LogInformation("Configuring chunk index '{IndexName}'", indexName);

        Meilisearch.Index index = await EnsureIndexCreatedAsync(indexName, cancellationToken);
        await EnsureCompatiblePrimaryKeyAsync(index, indexName, cancellationToken);

        TaskInfo settingsTask = await index.UpdateSettingsAsync(new Settings {
            SearchableAttributes = ["title", "sectionTitle", "content"],
            FilterableAttributes = ["documentId", "sourceId", "sourceType", "extension", "categoryIds"],
            DisplayedAttributes = ["*"]
        });
        await WaitAsync(settingsTask.TaskUid, cancellationToken);
        _logger.LogInformation("Chunk index '{IndexName}' configured", indexName);
    }

    private async Task<Meilisearch.Index> EnsureIndexCreatedAsync(string indexName, CancellationToken cancellationToken)
    {
        try {
            return await _client.GetIndexAsync(indexName, cancellationToken);
        } catch(MeilisearchApiError ex) when(ex.Code is "missing_authorization_header" or "invalid_api_key") {
            throw new MeilisearchAuthenticationException(ex);
        } catch(MeilisearchApiError ex) when(ex.Code == "index_not_found") {
            _ = ex;
        }

        TaskInfo createTask = await _client.CreateIndexAsync(indexName, "id");
        TaskResource createResult = await WaitForTaskRawAsync(createTask.TaskUid, cancellationToken);

        if(createResult.Status == TaskInfoStatus.Succeeded) {
            Meilisearch.Index created = await _client.GetIndexAsync(indexName, cancellationToken);
            if(string.IsNullOrEmpty(created.PrimaryKey))
                _logger.LogWarning(
                    "Chunk index '{IndexName}' was created with primaryKey='id' but GetIndexAsync returned null PrimaryKey. " +
                    "EnsureCompatiblePrimaryKeyAsync will attempt to recover.", indexName);
            return created;
        }

        if(createResult.Error != null && createResult.Error.TryGetValue("code", out string? code) && code == "index_already_exists") {
            _logger.LogInformation("Chunk index '{IndexName}' was already created concurrently; verifying compatibility", indexName);
            Meilisearch.Index existing;
            try {
                existing = await _client.GetIndexAsync(indexName, cancellationToken);
            } catch(MeilisearchApiError ex) when(ex.Code is "missing_authorization_header" or "invalid_api_key") {
                throw new MeilisearchAuthenticationException(ex);
            }
            await EnsureCompatiblePrimaryKeyAsync(existing, indexName, cancellationToken);
            return existing;
        }

        throw new InvalidOperationException(
            $"Meilisearch create chunk index task {createTask.TaskUid}: {createResult.Status}; {System.Text.Json.JsonSerializer.Serialize(createResult.Error)}");
    }

    private async Task<TaskResource> WaitForTaskRawAsync(int taskUid, CancellationToken cancellationToken)
    {
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_documentOptions.TaskTimeoutSeconds));
        try {
            return await _client.WaitForTaskAsync(taskUid,
                timeoutMs: _documentOptions.TaskTimeoutSeconds * 1000d,
                intervalMs: 100, cancellationToken: cts.Token);
        } catch(MeilisearchApiError ex) when(ex.Code is "missing_authorization_header" or "invalid_api_key") {
            throw new MeilisearchAuthenticationException(ex);
        } catch(OperationCanceledException ex) when(!cancellationToken.IsCancellationRequested) {
            throw new TimeoutException($"Meilisearch chunk create task {taskUid} did not finish within {_documentOptions.TaskTimeoutSeconds}s.", ex);
        }
    }

    public async Task UpsertChunksAsync(IEnumerable<SearchChunk> chunks, CancellationToken cancellationToken = default)
    {
        if(!_hybridOptions.Enabled) return;
        await EnsureIndexAsync(cancellationToken);
        Meilisearch.Index index = _client.Index(_hybridOptions.ChunkIndexName);
        List<SearchChunk> batch = chunks.ToList();
        if(batch.Count == 0) return;

        try {
            TaskInfo task = await index.AddDocumentsAsync(batch, primaryKey: "id");
            // Meilisearch generates embeddings inside the indexing task via REST calls to the
            // embedding server. This can take many minutes for large batches. We submit the task
            // and return immediately; Meilisearch processes it asynchronously in FIFO order.
            _logger.LogInformation("Submitted chunk upsert task {TaskUid} for {Count} chunks (embedding generation pending)",
                task.TaskUid, batch.Count);
        } catch(MeilisearchApiError ex) when(ex.Code is "missing_authorization_header" or "invalid_api_key") {
            throw new MeilisearchAuthenticationException(ex);
        }
    }

    public async Task DeleteChunksByDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        if(!_hybridOptions.Enabled) return;
        await EnsureIndexAsync(cancellationToken);
        Meilisearch.Index index = _client.Index(_hybridOptions.ChunkIndexName);
        DeleteDocumentsQuery query = new Meilisearch.QueryParameters.DeleteDocumentsQuery { Filter = $"documentId = \"{documentId}\"" };
        TaskInfo task = await index.DeleteDocumentsAsync(query, cancellationToken);
        // Submit and return; Meilisearch processes this before any subsequently submitted upsert
        // task due to FIFO ordering within the index.
        _logger.LogDebug("Submitted chunk delete task {TaskUid} for document {DocumentId}", task.TaskUid, documentId);
    }

    public async Task ReplaceDocumentChunksAsync(string documentId, IEnumerable<SearchChunk> newChunks, CancellationToken cancellationToken = default)
    {
        if(!_hybridOptions.Enabled) return;
        await EnsureIndexAsync(cancellationToken);
        List<SearchChunk> chunkList = newChunks.ToList();

        // Submit delete then upsert without waiting between them. Meilisearch guarantees FIFO
        // task ordering within an index, so the delete always runs before the upsert.
        await DeleteChunksByDocumentAsync(documentId, cancellationToken);
        if(chunkList.Count > 0)
            await UpsertChunksAsync(chunkList, cancellationToken);
    }

    public async Task DeleteChunkAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        if(!_hybridOptions.Enabled) return;
        await EnsureIndexAsync(cancellationToken);
        Meilisearch.Index index = _client.Index(_hybridOptions.ChunkIndexName);
        TaskInfo task = await index.DeleteOneDocumentAsync(chunkId);
        _logger.LogDebug("Submitted chunk delete task {TaskUid} for chunk {ChunkId}", task.TaskUid, chunkId);
    }

    private async Task WaitAsync(int taskUid, CancellationToken cancellationToken)
    {
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_documentOptions.TaskTimeoutSeconds));
        TaskResource result;
        try {
            result = await _client.WaitForTaskAsync(taskUid,
                timeoutMs: _documentOptions.TaskTimeoutSeconds * 1000d,
                intervalMs: 100, cancellationToken: cts.Token);
        } catch(OperationCanceledException ex) when(!cancellationToken.IsCancellationRequested) {
            throw new TimeoutException($"Meilisearch task {taskUid} timed out after {_documentOptions.TaskTimeoutSeconds}s.", ex);
        }
        if(result.Status != TaskInfoStatus.Succeeded)
            throw new InvalidOperationException($"Meilisearch task {taskUid}: {result.Status}; {System.Text.Json.JsonSerializer.Serialize(result.Error)}");
    }
}
