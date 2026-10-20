using Lantern.Core.Crawling;

namespace Lantern.Core.Administration;

public sealed class FileCrawlerProfile
{
    public Guid CrawlerId { get; set; }
    public string RootPath { get; set; } = "";
    public bool Recursive { get; set; } = true;
    public bool UseAllSupportedFormats { get; set; } = true;
    public IReadOnlyList<string> IncludedExtensions { get; set; } = [];
    public IReadOnlyList<string> ExcludedDirectories { get; set; } = [];
    public int MaxFileSizeMB { get; set; } = 100;
    public int MaxConcurrentFiles { get; set; } = 2;

    public FileCrawlerConfiguration ToConfiguration(string name) => new()
    {
        CrawlerId = CrawlerId,
        Name = name,
        RootPath = RootPath,
        Recursive = Recursive,
        IncludedExtensions = UseAllSupportedFormats ? [] : IncludedExtensions,
        ExcludedDirectories = ExcludedDirectories,
        MaxFileSizeMB = MaxFileSizeMB,
        MaxConcurrentFiles = MaxConcurrentFiles
    };
}
