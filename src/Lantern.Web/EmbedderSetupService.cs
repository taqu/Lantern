using Lantern.Core.Search;
using Lantern.Embedding;
using Lantern.Infrastructure.Search;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lantern.Web;

/// <summary>
/// Waits for the embedding model to load, then configures the REST embedder on
/// the Meilisearch chunk index so that hybrid search queries can execute.
/// Runs once in the background after startup and does not block the web host.
/// </summary>
internal sealed class EmbedderSetupService : IHostedService
{
    private readonly EmbedderConfigurationService _embedderConfig;
    private readonly IChunkIndexingService _chunkIndexing;
    private readonly EmbeddingServiceStatus _embeddingStatus;
    private readonly IMeilisearchCredentialManager _credentialManager;
    private readonly EmbeddingOptions _embeddingOptions;
    private readonly ILogger<EmbedderSetupService> _logger;

    public EmbedderSetupService(
        EmbedderConfigurationService embedderConfig,
        IChunkIndexingService chunkIndexing,
        EmbeddingServiceStatus embeddingStatus,
        IMeilisearchCredentialManager credentialManager,
        IOptions<EmbeddingOptions> embeddingOptions,
        ILogger<EmbedderSetupService> logger)
    {
        _embedderConfig = embedderConfig;
        _chunkIndexing = chunkIndexing;
        _embeddingStatus = embeddingStatus;
        _credentialManager = credentialManager;
        _embeddingOptions = embeddingOptions.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(() => RunAsync(cancellationToken), CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try {
            while (!_credentialManager.IsReady && !cancellationToken.IsCancellationRequested)
                await Task.Delay(500, cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;

            while (_embeddingStatus.Status == ModelStatus.NotLoaded || _embeddingStatus.Status == ModelStatus.Loading) {
                if (cancellationToken.IsCancellationRequested) return;
                await Task.Delay(500, cancellationToken);
            }

            if (_embeddingStatus.Status == ModelStatus.Failed) {
                _logger.LogWarning("Embedding model failed to load; hybrid search embedder not configured. {Message}",
                    _embeddingStatus.Message);
                return;
            }

            try {
                await _chunkIndexing.EnsureIndexAsync(cancellationToken);
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                _logger.LogError(ex, "Failed to initialize chunk index for hybrid search. Embedder configuration skipped.");
                return;
            }

            string embedUrl = $"http://{_embeddingOptions.ListenAddress}:{_embeddingOptions.Port}/embed";
            await _embedderConfig.ConfigureEmbedderAsync(embedUrl, cancellationToken);
        } catch (OperationCanceledException) {
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to configure Meilisearch embedder for hybrid search.");
        }
    }
}
