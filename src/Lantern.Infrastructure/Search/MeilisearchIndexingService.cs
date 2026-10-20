using Lantern.Core.Search;
using Meilisearch;
using Microsoft.Extensions.Logging;

namespace Lantern.Infrastructure.Search;

public sealed class MeilisearchIndexingService : IDocumentIndexingService
{
    private readonly MeilisearchClient _client;
    private readonly MeilisearchOptions _options;
    private readonly ILogger<MeilisearchIndexingService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public MeilisearchIndexingService(MeilisearchClient client, MeilisearchOptions options, ILogger<MeilisearchIndexingService> logger)
    {
        if (string.IsNullOrWhiteSpace(options.IndexName) || options.MaxDocumentsPerBatch <= 0 ||
            options.MaxPayloadBytesPerBatch < 2 || options.TaskTimeoutSeconds <= 0)
            throw new ArgumentException("Invalid Meilisearch indexing options.", nameof(options));
        _client = client; _options = options; _logger = logger;
    }

    public async Task WaitAsync(int taskUid, string operation, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(_options.TaskTimeoutSeconds));
        TaskResource result;
        try
        {
            result = await _client.WaitForTaskAsync(taskUid, timeoutMs: _options.TaskTimeoutSeconds * 1000d,
                intervalMs: 100, cancellationToken: deadline.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException($"Meilisearch {operation}, task {taskUid} did not finish within {_options.TaskTimeoutSeconds} seconds. Its outcome is unknown.", ex); }
        catch (MeilisearchTimeoutError ex)
        { throw new TimeoutException($"Meilisearch {operation}, task {taskUid} timed out. Its outcome is unknown.", ex); }
        if (result.Status != TaskInfoStatus.Succeeded)
            throw new InvalidOperationException($"Meilisearch {operation}, task {taskUid}: {result.Status}; {System.Text.Json.JsonSerializer.Serialize(result.Error)}");
    }

    private async Task ConfigureAsync(string name, CancellationToken ct)
    {
        try { await _client.GetIndexAsync(name, ct); }
        catch (MeilisearchApiError ex) when (ex.Code == "index_not_found")
        {
            TaskInfo create = await _client.CreateIndexAsync(name, "id", ct);
            await WaitAsync(create.TaskUid, "create index", ct);
        }
        Meilisearch.Index index = await _client.GetIndexAsync(name, ct);
        if (index.PrimaryKey != "id") throw new InvalidOperationException($"Index {name} must use primary key id.");
        TaskInfo task = await index.UpdateSettingsAsync(new Settings
        {
            SearchableAttributes = new[] { "title", "content" },
            FilterableAttributes = new FilterableAttribute[] { "sourceId", "sourceType", "extension", "mimeType", "categoryIds" },
            SortableAttributes = new[] { "modifiedAt", "indexedAt", "size" },
            DisplayedAttributes = new[] { "*" }
        }, ct);
        await WaitAsync(task.TaskUid, "configure index", ct);
    }

    private async Task InitializeAsync(CancellationToken ct)
    {
        if (_initialized) return;
        await ConfigureAsync(_options.IndexName, ct);
        _initialized = true;
    }

    public async Task EnsureIndexAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { await InitializeAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    public Task UpsertAsync(SearchDocument document, CancellationToken cancellationToken = default) => UpsertBatchAsync(new[] { document }, cancellationToken);

    private async Task PopulateAsync(string name, IEnumerable<SearchDocument> documents, CancellationToken ct)
    {
        foreach (IReadOnlyList<SearchDocument> batch in DocumentBatcher.Split(documents.Select(DocumentNormalizer.Normalize), _options.MaxDocumentsPerBatch, _options.MaxPayloadBytesPerBatch))
        {
            ct.ThrowIfCancellationRequested();
            TaskInfo task = await _client.Index(name).AddDocumentsAsync(batch, "id", ct);
            await WaitAsync(task.TaskUid, "upsert documents", ct);
        }
    }

    public async Task UpsertBatchAsync(IEnumerable<SearchDocument> documents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        await _gate.WaitAsync(cancellationToken);
        try { await InitializeAsync(cancellationToken); await PopulateAsync(_options.IndexName, documents, cancellationToken); }
        finally { _gate.Release(); }
    }

    public Task DeleteAsync(string documentId, CancellationToken cancellationToken = default) => DeleteBatchAsync(new[] { documentId }, cancellationToken);

    public async Task DeleteBatchAsync(IEnumerable<string> documentIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await InitializeAsync(cancellationToken);
            IEnumerable<string> ids = documentIds.Select(id => { ArgumentException.ThrowIfNullOrWhiteSpace(id); return id; });
            foreach (IReadOnlyList<string> batch in DocumentBatcher.Split(ids, _options.MaxDocumentsPerBatch, _options.MaxPayloadBytesPerBatch))
            {
                TaskInfo task = await _client.Index(_options.IndexName).DeleteDocumentsAsync(batch, cancellationToken);
                await WaitAsync(task.TaskUid, "delete documents", cancellationToken);
            }
        }
        finally { _gate.Release(); }
    }

    public async Task RebuildAsync(IEnumerable<SearchDocument> documents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        await _gate.WaitAsync(cancellationToken);
        string temporary = $"{_options.IndexName}-rebuild-{Guid.NewGuid():N}";
        bool promotionSubmitted = false;
        bool promotionConfirmed = false;
        try
        {
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
        }
        finally
        {
            try
            {
                if (!promotionSubmitted || promotionConfirmed)
                {
                    TaskInfo cleanup = await _client.DeleteIndexAsync(temporary, CancellationToken.None);
                    await WaitAsync(cleanup.TaskUid, "remove temporary index", CancellationToken.None);
                }
                else _logger.LogWarning("Rebuild promotion outcome uncertain; retained index {Index} for inspection", temporary);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not clean up rebuild index {Index}", temporary); }
            finally { _gate.Release(); }
        }
    }
}
