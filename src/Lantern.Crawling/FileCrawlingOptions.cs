namespace Lantern.Crawling;

public sealed class FileCrawlingOptions
{
    public const string SectionName = "FileCrawling";
    public int QueueCapacity { get; init; } = 32;
    public bool FollowDirectoryLinks { get; init; } = false;
}
