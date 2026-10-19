using Lantern.Core.Search;
using Lantern.Infrastructure.Search;
using Microsoft.Extensions.Options;

namespace Lantern.IntegrationTests;

public sealed class TextChunkingServiceTests
{
    private static TextChunkingService CreateService(int targetTokens = 512, int maxTokens = 768)
    {
        var options = Options.Create(new ChunkingOptions
        {
            TargetTokens = targetTokens,
            MaxTokens = maxTokens,
            OverlapTokens = 64
        });
        return new TextChunkingService(options);
    }

    [Fact]
    public void Chunk_EmptyText_ReturnsEmpty()
    {
        var svc = CreateService();
        Assert.Empty(svc.Chunk(""));
        Assert.Empty(svc.Chunk("   "));
    }

    [Fact]
    public void Chunk_ShortText_ReturnsSingleChunk()
    {
        var svc = CreateService();
        var chunks = svc.Chunk("Hello world. This is a short document.");
        Assert.Single(chunks);
        Assert.Equal(0, chunks[0].ChunkIndex);
        Assert.False(string.IsNullOrEmpty(chunks[0].Content));
    }

    [Fact]
    public void Chunk_LongText_ReturnsMultipleChunks()
    {
        var svc = CreateService(targetTokens: 10, maxTokens: 20);
        // Build text that exceeds target token limit
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 50; i++)
            sb.Append($"Sentence number {i}. ");
        var chunks = svc.Chunk(sb.ToString());
        Assert.True(chunks.Count > 1);
    }

    [Fact]
    public void Chunk_ChunkIndexIsSequential()
    {
        var svc = CreateService(targetTokens: 5, maxTokens: 10);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 30; i++)
            sb.AppendLine($"Paragraph {i}. Some content here.\n");
        var chunks = svc.Chunk(sb.ToString());
        for (int i = 0; i < chunks.Count; i++)
            Assert.Equal(i, chunks[i].ChunkIndex);
    }

    [Fact]
    public void Chunk_ParagraphsWithHeadings_DetectsSectionTitle()
    {
        var svc = CreateService(targetTokens: 10, maxTokens: 20);
        var text = "# Introduction\n\nThis is the introduction paragraph.\n\n# Conclusion\n\nThis is the conclusion.";
        var chunks = svc.Chunk(text);
        // At least some chunks should have a section title
        Assert.Contains(chunks, c => c.SectionTitle != null);
    }

    [Fact]
    public void CountTokens_ShortText_ReturnsPositive()
    {
        var svc = CreateService();
        var count = svc.CountTokens("Hello world");
        Assert.True(count > 0);
    }

    [Fact]
    public void CountTokens_WithLlamaCounter_UsesInjectedCounter()
    {
        var svc = CreateService();
        int callCount = 0;
        svc.SetTokenCounter(text => { callCount++; return text.Length / 4; });
        svc.CountTokens("test text");
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void Chunk_JapaneseText_ReturnsChunks()
    {
        var svc = CreateService(targetTokens: 10, maxTokens: 20);
        var japanese = "これはテストです。日本語のテキストを使ってチャンキングをテストします。";
        var chunks = svc.Chunk(japanese);
        Assert.NotEmpty(chunks);
        Assert.True(chunks[0].Content.Length > 0);
    }

    [Fact]
    public void Chunk_StartOffset_IsNonNegative()
    {
        var svc = CreateService();
        var chunks = svc.Chunk("Some text content here.");
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.StartOffset >= 0);
            Assert.True(chunk.EndOffset >= chunk.StartOffset);
        }
    }
}
