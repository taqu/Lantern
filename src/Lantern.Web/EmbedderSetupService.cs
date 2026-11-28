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
internal sealed class EmbedderSetupService : BackgroundService
{
    private readonly EmbedderConfigurationService _embedderConfig;
    private readonly IChunkIndexingService _chunkIndexing;
    private readonly EmbeddingServiceStatus _embeddingStatus;
    private readonly IMeilisearchCredentialManager _credentialManager;
    private readonly EmbeddingOptions _embeddingOptions;
    private readonly ILogger<EmbedderSetupService> _logger;
    private readonly HybridSearchReadiness _readiness;
    private readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

    public EmbedderSetupService(
        EmbedderConfigurationService embedderConfig,
        IChunkIndexingService chunkIndexing,
        EmbeddingServiceStatus embeddingStatus,
        IMeilisearchCredentialManager credentialManager,
        IOptions<EmbeddingOptions> embeddingOptions,
        HybridSearchReadiness readiness,
        ILogger<EmbedderSetupService> logger)
    {
        _embedderConfig = embedderConfig;
        _chunkIndexing = chunkIndexing;
        _embeddingStatus = embeddingStatus;
        _credentialManager = credentialManager;
        _embeddingOptions = embeddingOptions.Value;
        _logger = logger;
        _readiness = readiness;
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
            if(dims != 384 || arr.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.Number ||
                !value.TryGetSingle(out float component) || !float.IsFinite(component))) {
                _logger.LogError(
                    "Embedding server returned an invalid {Dims}-dimensional vector at {Url}. Embedder not configured.",
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

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try {
            // 1. Wait for Meilisearch credentials
            while(!_credentialManager.IsReady && !cancellationToken.IsCancellationRequested)
                await Task.Delay(500, cancellationToken);
            if(cancellationToken.IsCancellationRequested) return;

            // 2. Wait for embedding model to load
            while(_embeddingStatus.Status == ModelStatus.NotLoaded || _embeddingStatus.Status == ModelStatus.Loading) {
                if(cancellationToken.IsCancellationRequested) return;
                await Task.Delay(500, cancellationToken);
            }
            if(_embeddingStatus.Status == ModelStatus.Failed) {
                _logger.LogWarning("Embedding model failed to load; hybrid search embedder not configured. {Message}",
                    _embeddingStatus.Message);
                return;
            }

            // 3. Ensure chunk index exists (retry on transient Meilisearch errors)
            while(!cancellationToken.IsCancellationRequested) {
                try {
                    await _chunkIndexing.EnsureIndexAsync(cancellationToken);
                    break;
                } catch(Exception ex) when(ex is not OperationCanceledException) {
                    _logger.LogError(ex, "Failed to initialize chunk index; retrying in 30 s.");
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                }
            }
            if(cancellationToken.IsCancellationRequested) return;

            // 4. Wait for the embedding HTTP server to begin listening
            await _embeddingStatus.Listening.WaitAsync(cancellationToken);
            if(cancellationToken.IsCancellationRequested) return;
            string embedUrl = _embeddingOptions.Endpoint.AbsoluteUri;

            // 5. Verify embedding server responds with a valid vector (retry until ready)
            while(!cancellationToken.IsCancellationRequested) {
                if(await VerifyEmbeddingServerAsync(embedUrl, cancellationToken))
                    break;
                _logger.LogInformation("Embedding server preflight failed; retrying in 30 s.");
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
            if(cancellationToken.IsCancellationRequested) return;

            // 6. Configure the REST embedder on the Meilisearch chunk index (retry on failure)
            while(!cancellationToken.IsCancellationRequested) {
                try {
                    await _embedderConfig.ConfigureEmbedderAsync(embedUrl, cancellationToken);
                    break;
                } catch(Exception ex) when(ex is not OperationCanceledException) {
                    _logger.LogError(ex, "Failed to configure Meilisearch embedder; retrying in 30 s.");
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                }
            }
            if(cancellationToken.IsCancellationRequested) return;

            // 7. Periodic semantic probe — sets readiness for the chunk indexing service
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            do {
                try {
                    await _embedderConfig.VerifyConnectionAsync(cancellationToken);
                    if(!_readiness.IsReady)
                        _logger.LogInformation("Meilisearch semantic query succeeded. Hybrid search ready.");
                    _readiness.SetReady(true);
                } catch(Exception ex) when(!cancellationToken.IsCancellationRequested) {
                    _readiness.SetReady(false);
                    if(IsRejectedUriError(ex))
                        _logger.LogError(
                            "EmbeddingNetworkPolicyRejected: Meilisearch cannot reach the REST embedding " +
                            "server because its SSRF protection has rejected the loopback URI. " +
                            "Start Meilisearch with --experimental-allowed-ip-networks 127.0.0.0/8 " +
                            "(or MEILI_EXPERIMENTAL_ALLOWED_IP_NETWORKS=127.0.0.0/8). " +
                            "Embedding endpoint: {EmbedUrl}",
                            _embeddingOptions.Endpoint.AbsoluteUri);
                    else
                        _logger.LogWarning(ex, "Meilisearch semantic probe failed. Hybrid indexing suspended.");
                }
            } while(await timer.WaitForNextTickAsync(cancellationToken));
        } catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
        } catch(Exception ex) {
            _logger.LogError(ex, "Unhandled error in embedder setup service.");
        }
    }

    private static bool IsRejectedUriError(Exception ex)
    {
        string msg = ex.Message;
        return msg.Contains("Rejected URI", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("bad uri", StringComparison.OrdinalIgnoreCase);
    }

    public override void Dispose()
    {
        _readiness.SetReady(false);
        base.Dispose();
        _httpClient.Dispose();
    }
}
