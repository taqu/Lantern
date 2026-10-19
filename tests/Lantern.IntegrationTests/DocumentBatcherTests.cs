using Lantern.Infrastructure.Search;

namespace Lantern.IntegrationTests;

public sealed class DocumentBatcherTests
{
    [Fact]
    public void SplitsByCount() => Assert.Equal(new[] { 2, 2, 1 }, DocumentBatcher.Split(Enumerable.Range(1, 5), 2, 100).Select(b => b.Count));

    [Fact]
    public void SplitsByExactSerializedPayloadIncludingArrayDelimiters()
    {
        Assert.Single(DocumentBatcher.Split(new[] { "a", "b" }, 10, 9));
        Assert.Equal(2, DocumentBatcher.Split(new[] { "a", "b" }, 10, 8).Count());
        Assert.Equal(2, DocumentBatcher.Split(new[] { "日本語", "日本語" }, 10, 22).Count());
    }

    [Fact]
    public void RejectsOversizedItems() => Assert.Throws<ArgumentException>(() => DocumentBatcher.Split(new[] { "long text" }, 10, 4).ToList());

    [Fact]
    public void EmptySourceHasNoBatches() => Assert.Empty(DocumentBatcher.Split(Array.Empty<string>(), 1, 10));
}
