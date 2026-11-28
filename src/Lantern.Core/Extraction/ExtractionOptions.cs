namespace Lantern.Core.Extraction;

public sealed class ExtractionOptions
{
    public const string SectionName = "DocumentExtraction";
    public int MaxFileSizeMB { get; set; } = 100;
    public int MaxExtractedCharacters { get; set; } = 5_000_000;
    public int TimeoutSeconds { get; set; } = 60;
    public int MaxConcurrentExtractions { get; set; } = 2;
}
