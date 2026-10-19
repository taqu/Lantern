namespace Lantern.Core.Search;

public sealed class HybridSearchOptions
{
    public const string SectionName = "HybridSearch";
    public bool Enabled { get; set; } = false;
    public string EmbedderName { get; set; } = "lantern-granite";
    public string ChunkIndexName { get; set; } = "lantern-chunks";
    public double SemanticRatio { get; set; } = 0.5;
}
