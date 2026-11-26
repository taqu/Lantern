using Lantern.Core.Search;
using Meilisearch;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lantern.Infrastructure.Search;

public sealed class EmbedderConfigurationService
{
    private IMeilisearchClientFactory _clientFactory;
    private MeilisearchClient _client = null!;
    private readonly HybridSearchOptions _options;
    private readonly MeilisearchOptions _documentOptions;
    private readonly ILogger<EmbedderConfigurationService> _logger;

    public EmbedderConfigurationService(
        IMeilisearchClientFactory clientFactory,
        IOptions<HybridSearchOptions> options,
        IOptions<MeilisearchOptions> documentOptions,
        ILogger<EmbedderConfigurationService> logger)
    {
        _clientFactory = clientFactory;
        _options = options.Value;
        _documentOptions = documentOptions.Value;
        _logger = logger;
    }

    private void EnsureClient()
    {
        if(null == _client) {
            _client = _clientFactory.GetIndexingClient();
        }
    }
    // How long to wait for the embedder settings update task to reach a terminal state.
    // Meilisearch 1.55+ runs embedding generation (for existing documents) inside the
    // settingsUpdate task, which can take many minutes for large indexes.  We only wait
    // long enough to detect an immediate failure; a still-processing task is treated as
    // accepted and continues running in the background.
    private const int EmbedderSettleSeconds = 10;

    private const string EmbedderDocumentTemplate = "{{doc.title}}\n{{doc.sectionTitle}}\n{{doc.content}}";
    private const int EmbedderDimensions = 384;

    private async Task<bool> IsAlreadyConfiguredAsync(Meilisearch.Index index, string embedderName, string embedUrl, CancellationToken cancellationToken)
    {
        try {
            Dictionary<string, Embedder>? current = await index.GetEmbeddersAsync(cancellationToken);
            if(current == null || !current.TryGetValue(embedderName, out Embedder? existing)) return false;
            return existing.Source == EmbedderSource.Rest
                && existing.Url == embedUrl
                && existing.Dimensions == EmbedderDimensions
                && existing.DocumentTemplate == EmbedderDocumentTemplate;
        } catch(Exception ex) when(ex is not OperationCanceledException) {
            _logger.LogDebug(ex, "Could not read current embedder configuration; will re-apply");
            return false;
        }
    }

    public async Task ConfigureEmbedderAsync(string embedUrl, CancellationToken cancellationToken = default)
    {
        string indexName = _options.ChunkIndexName;
        string embedderName = _options.EmbedderName;

        _logger.LogInformation("Configuring REST embedder '{EmbedderName}' on index '{IndexName}'", embedderName, indexName);

        EnsureClient();
        Meilisearch.Index index = _client.Index(indexName);

        if(await IsAlreadyConfiguredAsync(index, embedderName, embedUrl, cancellationToken)) {
            _logger.LogInformation(
                "REST embedder '{EmbedderName}' on index '{IndexName}' is already configured with matching URL and dimensions; skipping update to avoid re-embedding.",
                embedderName, indexName);
            return;
        }

        Dictionary<string, Embedder> embedders = new Dictionary<string, Embedder> {
            [embedderName] = new Embedder {
                Source = EmbedderSource.Rest,
                Url = embedUrl,
                Dimensions = EmbedderDimensions,
                Request = new Dictionary<string, object> { ["text"] = "{{text}}" },
                Response = new Dictionary<string, object> { ["embedding"] = "{{embedding}}" },
                DocumentTemplate = EmbedderDocumentTemplate,
                DocumentTemplateMaxBytes = 8192
            }
        };

        try {
            TaskInfo task = await index.UpdateEmbeddersAsync(embedders, cancellationToken);
            _logger.LogInformation("Embedder settings task {TaskUid} submitted; waiting up to {Seconds}s for quick completion or immediate failure",
                task.TaskUid, EmbedderSettleSeconds);

            TaskResource? result = await WaitUpToAsync(task.TaskUid, EmbedderSettleSeconds, cancellationToken);
            if(result == null) {
                // Still processing (long-running embedding generation) — the settings will be
                // applied when Meilisearch finishes re-embedding existing documents.
                _logger.LogInformation(
                    "Embedder settings task {TaskUid} is still processing (generating embeddings for existing documents). " +
                    "Hybrid search will become available once Meilisearch finishes the background indexing.",
                    task.TaskUid);
            } else if(result.Status == TaskInfoStatus.Succeeded) {
                _logger.LogInformation("Embedder '{EmbedderName}' configured on index '{IndexName}'", embedderName, indexName);
            } else {
                throw new InvalidOperationException(
                    $"Meilisearch embedder settings task {task.TaskUid} {result.Status}: {System.Text.Json.JsonSerializer.Serialize(result.Error)}");
            }
        } catch(MeilisearchApiError ex) when(ex.Code is "missing_authorization_header" or "invalid_api_key") {
            throw new MeilisearchAuthenticationException(ex);
        } catch(Exception ex) {
            _logger.LogError(ex, "Failed to configure embedder '{EmbedderName}' on index '{IndexName}'", embedderName, indexName);
            throw;
        }
    }

    // Returns the TaskResource if the task completed within maxSeconds, null if still processing.
    private async Task<TaskResource?> WaitUpToAsync(int taskUid, int maxSeconds, CancellationToken cancellationToken)
    {
        EnsureClient();
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(maxSeconds));
        try {
            return await _client.WaitForTaskAsync(taskUid,
                timeoutMs: maxSeconds * 1000d,
                intervalMs: 500, cancellationToken: cts.Token);
        } catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested) {
            return null;
        }
    }
}
