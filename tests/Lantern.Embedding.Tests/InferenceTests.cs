using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Lantern.Embedding.Tests;

public sealed class InferenceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(383)]
    [InlineData(385)]
    public void RejectsUnexpectedDimensions(int size) =>
        Assert.Throws<InvalidOperationException>(() => InferenceWorker.ValidateVector(new float[size]));

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(float.PositiveInfinity)]
    public void RejectsNonFiniteValues(float value)
    {
        var vector = new float[384];
        vector[^1] = value;
        Assert.Throws<InvalidOperationException>(() => InferenceWorker.ValidateVector(vector));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RejectsUnexpectedVectorCount(int count) =>
        Assert.Throws<InvalidOperationException>(() =>
            InferenceWorker.ValidateSingleEmbedding(Enumerable.Repeat(new float[384], count).ToArray()));

    [Fact]
    public void RequiresVectorAndPreservesValidOutput()
    {
        Assert.Throws<InvalidOperationException>(() => InferenceWorker.ValidateVector(null));
        var vector = new float[384];
        vector[0] = 1;
        Assert.Same(vector, InferenceWorker.ValidateVector(vector));
    }

    [Fact]
    public async Task FullQueuePreservesAcceptedRequestAndPropagatesCallerCancellation()
    {
        var queue = new InferenceQueue(1);
        var item = new EmbeddingRequestItem("first", new(), default);
        Assert.True(await queue.TryEnqueueAsync(item, TimeSpan.FromSeconds(1), default));
        Assert.False(await queue.TryEnqueueAsync(item, TimeSpan.Zero, default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await queue.TryEnqueueAsync(item, TimeSpan.FromSeconds(1), new CancellationToken(true)));
        Assert.Same(item, await queue.Reader.ReadAsync());
    }

    [Fact]
    public async Task MissingModelFailsQueuedWorkAndWorkerStops()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders().AddConsole();
        builder.Services.Configure<EmbeddingOptions>(o => o.ModelPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.gguf"));
        builder.Services.AddSingleton(new InferenceQueue(1));
        builder.Services.AddSingleton<EmbeddingServiceStatus>();
        builder.Services.AddHostedService<InferenceWorker>();
        using var host = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.StartAsync(timeout.Token);
        var queue = host.Services.GetRequiredService<InferenceQueue>();
        var item = new EmbeddingRequestItem("test", new(TaskCreationOptions.RunContinuationsAsynchronously), default);
        Assert.True(await queue.TryEnqueueAsync(item, TimeSpan.FromSeconds(1), timeout.Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => item.Result.Task.WaitAsync(timeout.Token));
        Assert.Equal(ModelStatus.Failed, host.Services.GetRequiredService<EmbeddingServiceStatus>().Status);
        await host.StopAsync(timeout.Token);
    }
}

