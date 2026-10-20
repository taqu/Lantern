namespace Lantern.Core.Jobs;

public sealed record JobProgress
{
    public long DiscoveredFiles { get; init; }
    public long ProcessedFiles { get; init; }
    public long IndexedFiles { get; init; }
    public long UnchangedFiles { get; init; }
    public long SkippedFiles { get; init; }
    public long FailedFiles { get; init; }
    public long DeletedFiles { get; init; }

    public static readonly JobProgress Empty = new();
}
