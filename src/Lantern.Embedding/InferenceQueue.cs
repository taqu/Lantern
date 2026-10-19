using System.Threading.Channels;

namespace Lantern.Embedding;

public sealed record EmbeddingRequestItem(
    string Text,
    TaskCompletionSource<float[]> Result,
    CancellationToken CancellationToken);

public sealed class InferenceQueue
{
    private readonly Channel<EmbeddingRequestItem> _channel;

    public InferenceQueue(int capacity)
    {
        _channel = Channel.CreateBounded<EmbeddingRequestItem>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ChannelReader<EmbeddingRequestItem> Reader => _channel.Reader;

    public async ValueTask<bool> TryEnqueueAsync(EmbeddingRequestItem item, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            await _channel.Writer.WriteAsync(item, cts.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false; // timeout
        }
    }

    public void Complete() => _channel.Writer.Complete();
}
