using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lantern.Embedding;

public sealed class EmbeddingServer : BackgroundService
{
    private readonly EmbeddingOptions _options;
    private readonly InferenceQueue _queue;
    private readonly EmbeddingServiceStatus _status;
    private readonly ILogger<EmbeddingServer> _logger;
    private const int MaxRequestBodyBytes = 1024 * 64; // 64KB
    private const int MaxTextLength = 32768;

    public EmbeddingServer(
        IOptions<EmbeddingOptions> options,
        InferenceQueue queue,
        EmbeddingServiceStatus status,
        ILogger<EmbeddingServer> logger)
    {
        _options = options.Value;
        _queue = queue;
        _status = status;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string url = _options.Endpoint.GetLeftPart(UriPartial.Authority);
        _logger.LogInformation("Starting embedding server on {Url}", url);

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions {
            Args = new[] { "--urls", url }
        });
        builder.Logging.ClearProviders(); // Avoid log duplication

        WebApplication app = builder.Build();

        app.Lifetime.ApplicationStarted.Register(_status.SetListening);
        app.MapPost("/embed", (Delegate)HandleEmbedAsync)
            .AddEndpointFilter(async (context, next) => {
                var started = System.Diagnostics.Stopwatch.StartNew();
                var result = await next(context);
                _logger.LogDebug("POST /embed {TraceId} from {RemoteAddress} returned {StatusCode} in {ElapsedMs} ms",
                    context.HttpContext.TraceIdentifier, context.HttpContext.Connection.RemoteIpAddress,
                    (result as IStatusCodeHttpResult)?.StatusCode, started.ElapsedMilliseconds);
                return result;
            });
        app.MapGet("/health", (Delegate)HandleHealth);
        app.MapGet("/ready", (Delegate)HandleReady);

        try {
            await app.RunAsync(stoppingToken);
        } catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) {
            _logger.LogInformation("Embedding server stopped");
        }
    }

    internal async Task<IResult> HandleEmbedAsync(HttpContext context)
    {
        using var scope = _logger.BeginScope("Embedding request {TraceId}", context.TraceIdentifier);
        context.Request.Body = new LimitedStream(context.Request.Body, MaxRequestBodyBytes);

        EmbedRequest? request;
        try {
            request = await JsonSerializer.DeserializeAsync<EmbedRequest>(
                context.Request.Body,
                EmbedJsonContext.Default.EmbedRequest, context.RequestAborted);
        } catch(Exception ex) when(ex is JsonException or InvalidOperationException) {
            _logger.LogWarning("Rejected malformed embedding request");
            return Results.BadRequest(new { error = "Invalid JSON" });
        }

        if(request == null || string.IsNullOrWhiteSpace(request.Text)) {
            _logger.LogWarning("Rejected embedding request with missing or empty text");
            return Results.BadRequest(new { error = "Missing or empty 'text' field" });
        }

        if(request.Text.Length > MaxTextLength)
            return Results.BadRequest(new { error = $"Text too long (max {MaxTextLength} characters)" });

        if(!_status.IsReady) {
            _logger.LogWarning("Embedding server unavailable with model status {Status}", _status.Status);
            return Results.StatusCode(503);
        }

        TaskCompletionSource<float[]> tcs = new TaskCompletionSource<float[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));

        EmbeddingRequestItem item = new EmbeddingRequestItem(request.Text, tcs, cts.Token);
        try {
            if(!await _queue.TryEnqueueAsync(item, TimeSpan.FromSeconds(5), cts.Token)) {
                _logger.LogWarning("Embedding queue full for request {TraceId}", context.TraceIdentifier);
                return Results.StatusCode(503);
            }
            float[] vector = await tcs.Task.WaitAsync(cts.Token);
            return Results.Ok(new EmbedResponse(vector));
        } catch(OperationCanceledException) when(!context.RequestAborted.IsCancellationRequested) {
            _logger.LogWarning("Embedding request {TraceId} exceeded {Seconds}s", context.TraceIdentifier, _options.RequestTimeoutSeconds);
            return Results.StatusCode(503);
        } catch(Exception ex) when(ex is not OperationCanceledException) {
            _logger.LogWarning(ex, "Embedding request failed");
            return Results.StatusCode(500);
        }
    }

    private IResult HandleHealth() => Results.Ok(new { status = "ok" });

    private IResult HandleReady() => _status.IsReady
        ? Results.Ok(new { status = "ready" })
        : Results.StatusCode(503);
}

internal sealed record EmbedRequest([property: JsonPropertyName("text")] string? Text);
internal sealed record EmbedResponse([property: JsonPropertyName("embedding")] float[] Embedding);

[JsonSerializable(typeof(EmbedRequest))]
[JsonSerializable(typeof(EmbedResponse))]
internal sealed partial class EmbedJsonContext : JsonSerializerContext { }

internal sealed class LimitedStream : Stream
{
    private readonly Stream _inner;
    private readonly long _limit;
    private long _read;

    public LimitedStream(Stream inner, long limit) { _inner = inner; _limit = limit; }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        long remaining = _limit - _read;
        if(remaining <= 0) throw new InvalidOperationException("Request body too large");
        int toRead = (int)Math.Min(count, remaining);
        int n = _inner.Read(buffer, offset, toRead);
        _read += n;
        return n;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        long remaining = _limit - _read;
        if(remaining <= 0) throw new InvalidOperationException("Request body too large");
        int toRead = (int)Math.Min(count, remaining);
        int n = await _inner.ReadAsync(buffer, offset, toRead, ct);
        _read += n;
        return n;
    }

    public override void Flush() => _inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
