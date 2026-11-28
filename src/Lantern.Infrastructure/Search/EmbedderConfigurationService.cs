using System.Text.Json.Nodes;
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
                && existing.DocumentTemplate == EmbedderDocumentTemplate
                && existing.DocumentTemplateMaxBytes == 8192
                && JsonNode.DeepEquals(System.Text.Json.JsonSerializer.SerializeToNode(existing.Request),
                    new JsonObject { ["text"] = "{{text}}" })
                && JsonNode.DeepEquals(System.Text.Json.JsonSerializer.SerializeToNode(existing.Response),
                    new JsonObject { ["embedding"] = "{{embedding}}" });
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
            _logger.LogInformation("Waiting for embedder settings task {TaskUid}", task.TaskUid);
            TaskResource result;
            do {
                result = await _client.GetTaskAsync(task.TaskUid, cancellationToken);
                if(result.Status is TaskInfoStatus.Enqueued or TaskInfoStatus.Processing)
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            } while(result.Status is TaskInfoStatus.Enqueued or TaskInfoStatus.Processing);
            if(result.Status == TaskInfoStatus.Succeeded) {
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

    public async Task VerifyConnectionAsync(CancellationToken cancellationToken)
    {
        await _clientFactory.GetSearchClient().Index(_options.ChunkIndexName).SearchAsync<object>("test", new SearchQuery {
            Limit = 1,
            Hybrid = new HybridSearch { Embedder = _options.EmbedderName, SemanticRatio = 1 }
        }, cancellationToken);
    }
}
