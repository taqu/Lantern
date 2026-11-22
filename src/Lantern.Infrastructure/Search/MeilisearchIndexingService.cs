using Lantern.Core.Search;
using Meilisearch;
using Microsoft.Extensions.Logging;

namespace Lantern.Infrastructure.Search;

public sealed class MeilisearchIndexingService : IDocumentIndexingService
{
    private readonly IMeilisearchClientFactory _clientFactory;
    private MeilisearchClient? _client;
    private readonly MeilisearchOptions _options;
    private readonly ILogger<MeilisearchIndexingService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public MeilisearchIndexingService(IMeilisearchClientFactory clientFactory, MeilisearchOptions options, ILogger<MeilisearchIndexingService> logger)
    {
        if(string.IsNullOrWhiteSpace(options.IndexName) || options.MaxDocumentsPerBatch <= 0 ||
            options.MaxPayloadBytesPerBatch < 2 || options.TaskTimeoutSeconds <= 0) {
            throw new ArgumentException("Invalid Meilisearch indexing options.", nameof(options));
        }
        _clientFactory = clientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task WaitAsync(int taskUid, string operation, CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(_options.TaskTimeoutSeconds));
        TaskResource result;
        try {
            result = await _client.WaitForTaskAsync(taskUid, timeoutMs: _options.TaskTimeoutSeconds * 1000d,
                intervalMs: 100, cancellationToken: deadline.Token);
        } catch(MeilisearchApiError ex) when(ex.Code is "missing_authorization_header" or "invalid_api_key") { throw new MeilisearchAuthenticationException(ex); } catch(OperationCanceledException ex) when(!cancellationToken.IsCancellationRequested) { throw new TimeoutException($"Meilisearch {operation}, task {taskUid} did not finish within {_options.TaskTimeoutSeconds} seconds. Its outcome is unknown.", ex); } catch(MeilisearchTimeoutError ex) { throw new TimeoutException($"Meilisearch {operation}, task {taskUid} timed out. Its outcome is unknown.", ex); }
        if(result.Status != TaskInfoStatus.Succeeded)
            throw new InvalidOperationException($"Meilisearch {operation}, task {taskUid}: {result.Status}; {System.Text.Json.JsonSerializer.Serialize(result.Error)}");
    }

    private async Task EnsureCompatiblePrimaryKeyAsync(Meilisearch.Index index, string name, CancellationToken ct)
    {
        if(index.PrimaryKey == "id") return;
        if(!string.IsNullOrEmpty(index.PrimaryKey))
            throw new InvalidOperationException($"Index '{name}' exists with incompatible primary key '{index.PrimaryKey}'.");

        // Primary key is unset in the metadata DTO. Re-fetch current metadata to confirm
        // the index still exists and get its latest state before attempting any writes.
        Meilisearch.Index current;
        try {
            current = await _client!.GetIndexAsync(name, ct);
        } catch(MeilisearchApiError ex) when(ex.Code is "missing_authorization_header" or "invalid_api_key") {
            throw new MeilisearchAuthenticationException(ex);
        } catch(MeilisearchApiError ex) when(ex.Code == "index_not_found") {
            throw new InvalidOperationException(
                $"Index '{name}' disappeared between creation and primary-key assignment. " +
                $"Meilisearch error code: {ex.Code}", ex);
        }

        if(current.PrimaryKey == "id") return;
        if(!string.IsNullOrEmpty(current.PrimaryKey))
            throw new InvalidOperationException(
                $"Index '{name}' primary key was concurrently set to '{current.PrimaryKey}'; expected 'id'.");

        // Index exists with no primary key set. Check whether it is safe to assign one.
        // Use _client!.Index() for a live SDK-managed reference, not the deserialized DTO.
        IndexStats stats;
        try {
            stats = await _client!.Index(name).GetStatsAsync();
        } catch(MeilisearchApiError ex) when(ex.Code is "missing_authorization_header" or "invalid_api_key") {
            throw new MeilisearchAuthenticationException(ex);
        } catch(MeilisearchApiError ex) when(ex.Code == "index_not_found") {
            throw new InvalidOperationException(
                $"Index '{name}' was confirmed to exist but returned index_not_found when fetching stats. " +
                $"Unexpected Meilisearch state. Code: {ex.Code}", ex);
        } catch(Exception ex) when(ex is not OperationCanceledException) {
            throw new InvalidOperationException(
                $"Index '{name}' has no primary key set but fetching its statistics failed. " +
                "Cannot safely assign primary key 'id' without knowing whether the index contains documents. " +
                $"Underlying error ({ex.GetType().Name}): {ex.Message}", ex);
        }

        if(stats.NumberOfDocuments > 0)
            throw new InvalidOperationException(
                $"Index '{name}' has an unset primary key and contains {stats.NumberOfDocuments} document(s). " +
                "Cannot safely assign primary key 'id'. Set the primary key manually in Meilisearch before crawling.");

        TaskInfo setPkTask = await _client!.UpdateIndexAsync(name, "id", ct);
        TaskResource setPkResult = await WaitForTaskRawAsync(setPkTask.TaskUid, "set primary key", ct);
        if(setPkResult.Status != TaskInfoStatus.Succeeded)
            throw new InvalidOperationException(
                $"Index '{name}': failed to set primary key: task {setPkTask.TaskUid} {setPkResult.Status}; {System.Text.Json.JsonSerializer.Serialize(setPkResult.Error)}");
        _logger.LogInformation("Assigned primary key 'id' on index '{Name}' that had no primary key set", name);
    }

    // Maximum seconds to wait for the one-time embedder reset on the parent index.
    // Meilisearch must delete vector data for all existing documents, which can be slow
    // on large indexes. This only runs once per index lifetime (when a stale embedder is found).
    private const int EmbedderResetTimeoutSeconds = 300;

    private async Task ConfigureAsync(string name, CancellationToken ct)
    {
        if(null == _client) {
            _client = _clientFactory.GetIndexingClient();
            if(null == _client) {
                throw new Exception("cannot get indexing client");
            }
        }
        Meilisearch.Index index = await EnsureIndexCreatedAsync(name, ct);
        await EnsureCompatiblePrimaryKeyAsync(index, name, ct);
        TaskInfo task = await index.UpdateSettingsAsync(new Settings {
            SearchableAttributes = new[] { "title", "content" },
            FilterableAttributes = new FilterableAttribute[] { "sourceId", "sourceType", "extension", "mimeType", "categoryIds" },
            SortableAttributes = new[] { "modifiedAt", "indexedAt", "size" },
            DisplayedAttributes = new[] { "*" }
        }, ct);
        await WaitAsync(task.TaskUid, "configure index", ct);
        await EnsureNoEmbeddersAsync(name, ct);
    }

    // The parent document index is keyword-only; hybrid search embeddings belong on the chunk index.
    // If a stale embedder was accidentally configured here, remove it so document upserts do not
    // trigger embedding inference and cause task timeouts.
    private async Task EnsureNoEmbeddersAsync(string name, CancellationToken ct)
    {
        Dictionary<string, Embedder>? embedders;
        try {
            embedders = await _client!.Index(name).GetEmbeddersAsync(ct);
        } catch(MeilisearchApiError ex) when(ex.Code is "missing_authorization_header" or "invalid_api_key") {
            throw new MeilisearchAuthenticationException(ex);
        } catch(Exception ex) when(ex is not OperationCanceledException) {
            _logger.LogWarning(ex, "Could not check embedders on parent index '{Name}'; skipping reset", name);
            return;
        }

        if(embedders == null || embedders.Count == 0) return;

        _logger.LogWarning(
            "Parent index '{Name}' has {Count} embedder(s) configured ({Names}); removing them. " +
            "Embeddings belong on the chunk index only. This reset may take several minutes on large indexes.",
            name, embedders.Count, string.Join(", ", embedders.Keys));

        TaskInfo resetTask;
        try {
            resetTask = await _client!.Index(name).ResetEmbeddersAsync(ct);
        } catch(MeilisearchApiError ex) when(ex.Code is "missing_authorization_header" or "invalid_api_key") {
            throw new MeilisearchAuthenticationException(ex);
        }

        _logger.LogInformation("Submitted embedder reset task {TaskUid} on parent index '{Name}'; waiting up to {Seconds}s",
            resetTask.TaskUid, name, EmbedderResetTimeoutSeconds);

        TaskResource result = await WaitForTaskRawAsync(resetTask.TaskUid, "reset parent embedders",
            EmbedderResetTimeoutSeconds, ct);
        if(result.Status != TaskInfoStatus.Succeeded)
            throw new InvalidOperationException(
                $"Parent index '{name}': embedder reset task {resetTask.TaskUid} {result.Status}; " +
                $"{System.Text.Json.JsonSerializer.Serialize(result.Error)}");

        _logger.LogInformation("Embedder(s) removed from parent index '{Name}'; index is now keyword-only", name);
    }

    private async Task<Meilisearch.Index> EnsureIndexCreatedAsync(string name, CancellationToken ct)
    {
        // Check existence first to avoid unnecessary creation attempts
        try {
            return await _client.GetIndexAsync(name, ct);
        } catch(MeilisearchApiError ex) when(ex.Code is "missing_authorization_header" or "invalid_api_key") {
            throw new MeilisearchAuthenticationException(ex);
        } catch(MeilisearchApiError ex) when(ex.Code == "index_not_found") {
            // Fall through to create
            _ = ex;
        }

        TaskInfo createTask = await _client.CreateIndexAsync(name, "id", ct);
        TaskResource createResult = await WaitForTaskRawAsync(createTask.TaskUid, "create index", ct);

        if(createResult.Status == TaskInfoStatus.Succeeded) {
            Meilisearch.Index created = await _client.GetIndexAsync(name, ct);
            if(string.IsNullOrEmpty(created.PrimaryKey))
                _logger.LogWarning(
                    "Index '{Name}' was created with primaryKey='id' but GetIndexAsync returned null PrimaryKey. " +
                    "EnsureCompatiblePrimaryKeyAsync will attempt to recover.", name);
            return created;
        }

        // Concurrent startup may have already created the index — treat this as benign
        if(createResult.Error != null && createResult.Error.TryGetValue("code", out string? code) && code == "index_already_exists") {
            _logger.LogInformation("Index '{Name}' was already created concurrently; verifying compatibility", name);
            Meilisearch.Index existing;
            try {
                existing = await _client.GetIndexAsync(name, ct);
            } catch(MeilisearchApiError ex) when(ex.Code is "missing_authorization_header" or "invalid_api_key") {
                throw new MeilisearchAuthenticationException(ex);
            }
            await EnsureCompatiblePrimaryKeyAsync(existing, name, ct);
            return existing;
        }

        throw new InvalidOperationException(
            $"Meilisearch create index task {createTask.TaskUid}: {createResult.Status}; {System.Text.Json.JsonSerializer.Serialize(createResult.Error)}");
    }

    private Task<TaskResource> WaitForTaskRawAsync(int taskUid, string operation, CancellationToken cancellationToken)
        => WaitForTaskRawAsync(taskUid, operation, _options.TaskTimeoutSeconds, cancellationToken);

    private async Task<TaskResource> WaitForTaskRawAsync(int taskUid, string operation, int timeoutSeconds, CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try {
            return await _client.WaitForTaskAsync(taskUid, timeoutMs: timeoutSeconds * 1000d,
                intervalMs: 100, cancellationToken: deadline.Token);
        } catch(MeilisearchApiError ex) when(ex.Code is "missing_authorization_header" or "invalid_api_key") {
            throw new MeilisearchAuthenticationException(ex);
        } catch(OperationCanceledException ex) when(!cancellationToken.IsCancellationRequested) {
            throw new TimeoutException($"Meilisearch {operation}, task {taskUid} did not finish within {timeoutSeconds} seconds.", ex);
        } catch(MeilisearchTimeoutError ex) {
            throw new TimeoutException($"Meilisearch {operation}, task {taskUid} timed out.", ex);
        }
    }

    private async Task InitializeAsync(CancellationToken ct)
    {
        if(_initialized) return;
        await ConfigureAsync(_options.IndexName, ct);
        _initialized = true;
    }

    public async Task EnsureIndexAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { await InitializeAsync(cancellationToken); } finally { _gate.Release(); }
    }

    public Task UpsertAsync(SearchDocument document, CancellationToken cancellationToken = default) => UpsertBatchAsync(new[] { document }, cancellationToken);

    private async Task PopulateAsync(string name, IEnumerable<SearchDocument> documents, CancellationToken ct)
    {
        if(null == _client) {
            return;
        }
        foreach(IReadOnlyList<SearchDocument> batch in DocumentBatcher.Split(documents.Select(DocumentNormalizer.Normalize), _options.MaxDocumentsPerBatch, _options.MaxPayloadBytesPerBatch)) {
            ct.ThrowIfCancellationRequested();
            try {
                TaskInfo task = await _client.Index(name).AddDocumentsAsync(batch, "id", ct);
                await WaitAsync(task.TaskUid, "upsert documents", ct);
            } catch(MeilisearchApiError ex) when(ex.Code is "missing_authorization_header" or "invalid_api_key") { throw new MeilisearchAuthenticationException(ex); }
        }
    }

    public async Task UpsertBatchAsync(IEnumerable<SearchDocument> documents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        await _gate.WaitAsync(cancellationToken);
        try { await InitializeAsync(cancellationToken); await PopulateAsync(_options.IndexName, documents, cancellationToken); } finally { _gate.Release(); }
    }

    public Task DeleteAsync(string documentId, CancellationToken cancellationToken = default) => DeleteBatchAsync(new[] { documentId }, cancellationToken);

    public async Task DeleteBatchAsync(IEnumerable<string> documentIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        await _gate.WaitAsync(cancellationToken);
        try {
            await InitializeAsync(cancellationToken);
            IEnumerable<string> ids = documentIds.Select(id => { ArgumentException.ThrowIfNullOrWhiteSpace(id); return id; });
            foreach(IReadOnlyList<string> batch in DocumentBatcher.Split(ids, _options.MaxDocumentsPerBatch, _options.MaxPayloadBytesPerBatch)) {
                TaskInfo task = await _client.Index(_options.IndexName).DeleteDocumentsAsync(batch, cancellationToken);
                await WaitAsync(task.TaskUid, "delete documents", cancellationToken);
            }
        } finally { _gate.Release(); }
    }

    public async Task RebuildAsync(IEnumerable<SearchDocument> documents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        await _gate.WaitAsync(cancellationToken);
        string temporary = $"{_options.IndexName}-rebuild-{Guid.NewGuid():N}";
        bool promotionSubmitted = false;
        bool promotionConfirmed = false;
        try {
            await ConfigureAsync(temporary, cancellationToken);
            await PopulateAsync(temporary, documents, cancellationToken);
            await InitializeAsync(cancellationToken);
            // Once submitted, a timeout/cancellation leaves promotion outcome uncertain.
            // Retain the temporary index in that case; it may hold the old active data.
            cancellationToken.ThrowIfCancellationRequested();
            promotionSubmitted = true;
            TaskInfo swap = await _client.SwapIndexesAsync(new List<IndexSwap> { new(_options.IndexName, temporary) }, cancellationToken);
            await WaitAsync(swap.TaskUid, "promote rebuilt index", cancellationToken);
            promotionConfirmed = true;
        } finally {
            try {
                if(!promotionSubmitted || promotionConfirmed) {
                    TaskInfo cleanup = await _client.DeleteIndexAsync(temporary, CancellationToken.None);
                    await WaitAsync(cleanup.TaskUid, "remove temporary index", CancellationToken.None);
                } else _logger.LogWarning("Rebuild promotion outcome uncertain; retained index {Index} for inspection", temporary);
            } catch(Exception ex) { _logger.LogWarning(ex, "Could not clean up rebuild index {Index}", temporary); } finally { _gate.Release(); }
        }
    }
}
