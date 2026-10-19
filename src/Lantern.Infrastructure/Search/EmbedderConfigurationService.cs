using Lantern.Core.Search;
using Meilisearch;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lantern.Infrastructure.Search;

public sealed class EmbedderConfigurationService
{
    private readonly MeilisearchClient _client;
    private readonly HybridSearchOptions _options;
    private readonly MeilisearchOptions _documentOptions;
    private readonly ILogger<EmbedderConfigurationService> _logger;

    public EmbedderConfigurationService(
        MeilisearchClient client,
        IOptions<HybridSearchOptions> options,
        IOptions<MeilisearchOptions> documentOptions,
        ILogger<EmbedderConfigurationService> logger)
    {
        _client = client;
        _options = options.Value;
        _documentOptions = documentOptions.Value;
        _logger = logger;
    }

    public async Task ConfigureEmbedderAsync(string embedUrl, CancellationToken cancellationToken = default)
    {
        string indexName = _options.ChunkIndexName;
        string embedderName = _options.EmbedderName;

        _logger.LogInformation("Configuring REST embedder '{EmbedderName}' on index '{IndexName}'", embedderName, indexName);

        var index = _client.Index(indexName);

        var embedders = new Dictionary<string, Embedder>
        {
            [embedderName] = new Embedder
            {
                Source = EmbedderSource.Rest,
                Url = embedUrl,
                Dimensions = 384,
                Request = new Dictionary<string, object> { ["text"] = "{{text}}" },
                Response = new Dictionary<string, object> { ["embedding"] = "{{embedding}}" },
                DocumentTemplate = "{{doc.title}}\n{{doc.sectionTitle}}\n{{doc.content}}",
                DocumentTemplateMaxBytes = 8192
            }
        };

        try
        {
            var task = await index.UpdateEmbeddersAsync(embedders, cancellationToken);
            await WaitAsync(task.TaskUid, cancellationToken);
            _logger.LogInformation("Embedder '{EmbedderName}' configured on index '{IndexName}'", embedderName, indexName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure embedder '{EmbedderName}' on index '{IndexName}'", embedderName, indexName);
            throw;
        }
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
