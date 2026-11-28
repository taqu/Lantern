namespace Lantern.Core.Search;

public sealed class ChunkingOptions
{
    public const string SectionName = "Chunking";
    public int TargetTokens { get; set; } = 512;
    public int MaxTokens { get; set; } = 768;
    public int OverlapTokens { get; set; } = 64;
}
