namespace Lantern.Core.Search;

public interface ITextChunkingService
{
    IReadOnlyList<TextChunk> Chunk(string text, ChunkingOptions? options = null);
    int CountTokens(string text);
}

public sealed record TextChunk(string Content, int ChunkIndex, int StartOffset, int EndOffset, string? SectionTitle = null);
