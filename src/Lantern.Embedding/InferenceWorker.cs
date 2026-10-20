using LlamaPure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lantern.Embedding;

public sealed class InferenceWorker : BackgroundService
{
    private readonly InferenceQueue _queue;
    private readonly EmbeddingOptions _options;
    private readonly EmbeddingServiceStatus _status;
    private readonly ILogger<InferenceWorker> _logger;
    private readonly string _modelPath;
    private const int ExpectedDimensions = 384;

    public InferenceWorker(
        InferenceQueue queue,
        IOptions<EmbeddingOptions> options,
        EmbeddingServiceStatus status,
        ILogger<InferenceWorker> logger,
        IHostEnvironment env)
    {
        _queue = queue;
        _options = options.Value;
        _status = status;
        _logger = logger;

        // Resolve model path relative to content root
        _modelPath = Path.IsPathRooted(_options.ModelPath)
            ? _options.ModelPath
            : Path.GetFullPath(Path.Combine(env.ContentRootPath, _options.ModelPath));
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run on a dedicated OS thread (native inference is blocking)
        return Task.Factory.StartNew(
            () => RunInferenceLoop(stoppingToken),
            stoppingToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
    }

    private async Task RunInferenceLoop(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Inference worker starting. Model path: {ModelPath}", _modelPath);

        if (!File.Exists(_modelPath))
        {
            _logger.LogWarning("Embedding model not found at '{ModelPath}'. Embedding disabled.", _modelPath);
            _status.SetFailed($"Model file not found: {_modelPath}");
            DrainQueueWithError("Embedding model not available", stoppingToken);
            return;
        }

        LlamaPureClient? client = null;
        try
        {
            _status.SetLoading();
            client = new LlamaPureClient(_modelPath, _options.ContextSize, _options.Threads);
            _status.SetReady();
            _logger.LogInformation("Embedding model loaded successfully. Dimensions: {Dims}", ExpectedDimensions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load embedding model from '{ModelPath}'", _modelPath);
            _status.SetFailed($"Model load failed: {ex.Message}");
            DrainQueueWithError("Model initialization failed", stoppingToken);
            return;
        }

        try
        {
            await foreach (EmbeddingRequestItem request in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                if (request.CancellationToken.IsCancellationRequested)
                {
                    request.Result.TrySetCanceled(request.CancellationToken);
                    continue;
                }

                try
                {
                    List<float[]> embeddings = client.GetEmbedding(request.Text, overlap: 0);
                    if (embeddings.Count == 0)
                    {
                        request.Result.TrySetException(new InvalidOperationException("Empty embedding result"));
                        continue;
                    }

                    float[] vector = embeddings[0]; // Use first chunk's embedding

                    if (vector.Length != ExpectedDimensions)
                    {
                        request.Result.TrySetException(new InvalidOperationException(
                            $"Expected {ExpectedDimensions} dimensions, got {vector.Length}"));
                        continue;
                    }

                    // Validate finite values
                    bool hasNonFinite = false;
                    foreach (float f in vector)
                    {
                        if (!float.IsFinite(f)) { hasNonFinite = true; break; }
                    }
                    if (hasNonFinite)
                    {
                        request.Result.TrySetException(new InvalidOperationException("Embedding contains non-finite values"));
                        continue;
                    }

                    request.Result.TrySetResult(vector);
                }
                catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
                {
                    request.Result.TrySetCanceled(request.CancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Inference failed for request");
                    request.Result.TrySetException(ex);
                    // Continue processing - don't terminate the worker on a single failure
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Inference worker stopping");
        }
        finally
        {
            _status.SetStopping();
            client?.Dispose();
        }
    }

    private void DrainQueueWithError(string message, CancellationToken stoppingToken)
    {
        Task.Run(async () =>
        {
            try
            {
                await foreach (EmbeddingRequestItem request in _queue.Reader.ReadAllAsync(stoppingToken))
                {
                    request.Result.TrySetException(new InvalidOperationException(message));
                }
            }
            catch (OperationCanceledException) { }
        }, CancellationToken.None);
    }
}
