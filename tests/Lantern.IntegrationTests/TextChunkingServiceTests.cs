using Lantern.Core.Search;
using Lantern.Infrastructure.Search;
using Microsoft.Extensions.Options;

namespace Lantern.IntegrationTests;

public sealed class TextChunkingServiceTests
{
    private static TextChunkingService CreateService(int targetTokens = 512, int maxTokens = 768)
    {
        IOptions<ChunkingOptions> options = Options.Create(new ChunkingOptions
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
        TextChunkingService svc = CreateService();
        Assert.Empty(svc.Chunk(""));
        Assert.Empty(svc.Chunk("   "));
    }

    [Fact]
    public void Chunk_ShortText_ReturnsSingleChunk()
    {
        TextChunkingService svc = CreateService();
        IReadOnlyList<TextChunk> chunks = svc.Chunk("Hello world. This is a short document.");
        Assert.Single(chunks);
        Assert.Equal(0, chunks[0].ChunkIndex);
        Assert.False(string.IsNullOrEmpty(chunks[0].Content));
    }

    [Fact]
    public void Chunk_LongText_ReturnsMultipleChunks()
    {
        TextChunkingService svc = CreateService(targetTokens: 10, maxTokens: 20);
        // Build text that exceeds target token limit
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 50; i++)
            sb.Append($"Sentence number {i}. ");
        IReadOnlyList<TextChunk> chunks = svc.Chunk(sb.ToString());
        Assert.True(chunks.Count > 1);
    }

    [Fact]
    public void Chunk_ChunkIndexIsSequential()
    {
        TextChunkingService svc = CreateService(targetTokens: 5, maxTokens: 10);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 30; i++)
            sb.AppendLine($"Paragraph {i}. Some content here.\n");
        IReadOnlyList<TextChunk> chunks = svc.Chunk(sb.ToString());
        for (int i = 0; i < chunks.Count; i++)
            Assert.Equal(i, chunks[i].ChunkIndex);
    }

    [Fact]
    public void Chunk_ParagraphsWithHeadings_DetectsSectionTitle()
    {
        TextChunkingService svc = CreateService(targetTokens: 10, maxTokens: 20);
        string text = "# Introduction\n\nThis is the introduction paragraph.\n\n# Conclusion\n\nThis is the conclusion.";
        IReadOnlyList<TextChunk> chunks = svc.Chunk(text);
        // At least some chunks should have a section title
        Assert.Contains(chunks, c => c.SectionTitle != null);
    }

    [Fact]
    public void CountTokens_ShortText_ReturnsPositive()
    {
        TextChunkingService svc = CreateService();
        int count = svc.CountTokens("Hello world");
        Assert.True(count > 0);
    }

    [Fact]
    public void CountTokens_WithLlamaCounter_UsesInjectedCounter()
    {
        TextChunkingService svc = CreateService();
        int callCount = 0;
        svc.SetTokenCounter(text => { callCount++; return text.Length / 4; });
        svc.CountTokens("test text");
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void Chunk_JapaneseText_ReturnsChunks()
    {
        TextChunkingService svc = CreateService(targetTokens: 10, maxTokens: 20);
        string japanese = "これはテストです。日本語のテキストを使ってチャンキングをテストします。";
        IReadOnlyList<TextChunk> chunks = svc.Chunk(japanese);
        Assert.NotEmpty(chunks);
        Assert.True(chunks[0].Content.Length > 0);
    }

    [Fact]
    public void Chunk_StartOffset_IsNonNegative()
    {
        TextChunkingService svc = CreateService();
        IReadOnlyList<TextChunk> chunks = svc.Chunk("Some text content here.");
        foreach (TextChunk chunk in chunks)
        {
            Assert.True(chunk.StartOffset >= 0);
            Assert.True(chunk.EndOffset >= chunk.StartOffset);
        }
    }
}
