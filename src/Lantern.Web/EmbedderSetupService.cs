using System.Net.Http;
using System.Text.Json;
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
    private readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

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

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _httpClient.Dispose();
        return Task.CompletedTask;
    }

    // Returns false and logs if the embedding server cannot produce a valid response.
    // This prevents Meilisearch from being pointed at an endpoint that will only fail.
    private async Task<bool> VerifyEmbeddingServerAsync(string embedUrl, CancellationToken cancellationToken)
    {
        const string probe = "test";
        try {
            using var request = new StringContent(
                $"{{\"text\":\"{probe}\"}}",
                System.Text.Encoding.UTF8,
                "application/json");
            using var response = await _httpClient.PostAsync(embedUrl, request, cancellationToken);
            if(!response.IsSuccessStatusCode) {
                _logger.LogError(
                    "Embedding server preflight failed: {Status} {Url}. Embedder not configured.",
                    (int)response.StatusCode, embedUrl);
                return false;
            }
            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), default, cancellationToken);
            if(!doc.RootElement.TryGetProperty("embedding", out var arr) ||
               arr.ValueKind != JsonValueKind.Array) {
                _logger.LogError(
                    "Embedding server response missing 'embedding' array at {Url}. Embedder not configured.",
                    embedUrl);
                return false;
            }
            int dims = arr.GetArrayLength();
            if(dims != 384) {
                _logger.LogError(
                    "Embedding server returned {Dims} dimensions (expected 384) at {Url}. Embedder not configured.",
                    dims, embedUrl);
                return false;
            }
            _logger.LogInformation("Embedding server preflight passed: {Dims}-dimensional vector returned from {Url}.", dims, embedUrl);
            return true;
        } catch(Exception ex) when(ex is not OperationCanceledException) {
            _logger.LogError(ex,
                "Embedding server at {Url} is unreachable. Confirm the server is running and the address is correct. Embedder not configured.",
                embedUrl);
            return false;
        }
    }

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

            if(!await VerifyEmbeddingServerAsync(embedUrl, cancellationToken))
                return;

            await _embedderConfig.ConfigureEmbedderAsync(embedUrl, cancellationToken);
        } catch (OperationCanceledException) {
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to configure Meilisearch embedder for hybrid search.");
        }
    }
}
