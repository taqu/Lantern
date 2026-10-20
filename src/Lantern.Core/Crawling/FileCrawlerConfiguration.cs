using System.ComponentModel.DataAnnotations;

namespace Lantern.Core.Crawling;

public sealed class FileCrawlerConfiguration
{
    public string CrawlerType { get; init; } = "file";
    public Guid CrawlerId { get; init; }
    public string Name { get; init; } = "";
    public string RootPath { get; init; } = "";
    public bool Recursive { get; init; } = true;
    public IReadOnlyList<string> IncludedExtensions { get; init; } = [];
    public IReadOnlyList<string> ExcludedDirectories { get; init; } = [];
    public int MaxFileSizeMB { get; init; } = 100;
    public int MaxConcurrentFiles { get; init; } = 2;

    public void Validate()
    {
        if (CrawlerId == Guid.Empty)
            throw new ValidationException("Crawler ID is required.");
        if (string.IsNullOrWhiteSpace(RootPath))
            throw new ValidationException("Root path is required.");
        if (MaxFileSizeMB <= 0)
            throw new ValidationException("MaxFileSizeMB must be positive.");
        if (MaxConcurrentFiles <= 0 || MaxConcurrentFiles > 32)
            throw new ValidationException("MaxConcurrentFiles must be between 1 and 32.");
    }
}
