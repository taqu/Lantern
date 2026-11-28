using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lantern.Embedding.Tests;

public sealed class EmbeddingServerTests
{
    private readonly EmbeddingServiceStatus _state = new();
    private readonly InferenceQueue _queue = new(1);

    private Task<IResult> Post(string json, CancellationToken ct = default)
    {
        var server = new EmbeddingServer(Options.Create(new EmbeddingOptions()), _queue, _state,
            NullLogger<EmbeddingServer>.Instance);
        var context = new DefaultHttpContext { RequestAborted = ct };
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return server.HandleEmbedAsync(context);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"text\":null}")]
    [InlineData("{\"text\":\"  \"}")]
    [InlineData("{\"text\":[\"one\",\"two\"]}")]
    public async Task InvalidRequestReturnsBadRequest(string json) =>
        Assert.Equal(400, ((IStatusCodeHttpResult)await Post(json)).StatusCode);

    [Fact]
    public async Task OversizedTextIsRejected() =>
        Assert.Equal(400, ((IStatusCodeHttpResult)await Post("{\"text\":\"" + new string('x', 32769) + "\"}")).StatusCode);

    [Fact]
    public async Task UnloadedModelReturnsUnavailable() =>
        Assert.Equal(503, ((IStatusCodeHttpResult)await Post("{\"text\":\"test\"}")).StatusCode);

    [Theory]
    [InlineData(true, 200)]
    [InlineData(false, 500)]
    public async Task InferenceResultControlsResponse(bool success, int expected)
    {
        _state.SetReady();
        var response = Post("{\"text\":\"パフォーマンス計測\"}");
        var request = await _queue.Reader.ReadAsync();
        Assert.Equal("パフォーマンス計測", request.Text);
        if(success) request.Result.SetResult(new float[384]);
        else request.Result.SetException(new InvalidOperationException("inference failed"));
        Assert.Equal(expected, ((IStatusCodeHttpResult)await response).StatusCode);
    }

    [Fact]
    public async Task ClientDisconnectCancelsQueuedWork()
    {
        _state.SetReady();
        using var cts = new CancellationTokenSource();
        var response = Post("{\"text\":\"test\"}", cts.Token);
        var item = await _queue.Reader.ReadAsync();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => response);
        Assert.True(item.CancellationToken.IsCancellationRequested);
    }
}
