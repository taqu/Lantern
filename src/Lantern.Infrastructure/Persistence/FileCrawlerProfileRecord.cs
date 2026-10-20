using Lantern.Core.Administration;

namespace Lantern.Infrastructure.Persistence;

public sealed class FileCrawlerProfileRecord
{
    public Guid CrawlerId { get; set; }
    public string RootPath { get; set; } = "";
    public bool Recursive { get; set; } = true;
    public bool UseAllSupportedFormats { get; set; } = true;
    public string? IncludedExtensions { get; set; }
    public string? ExcludedDirectories { get; set; }
    public int MaxFileSizeMB { get; set; } = 100;
    public int MaxConcurrentFiles { get; set; } = 2;

    public FileCrawlerProfile ToDomain() => new()
    {
        CrawlerId = CrawlerId,
        RootPath = RootPath,
        Recursive = Recursive,
        UseAllSupportedFormats = UseAllSupportedFormats,
        IncludedExtensions = ParseList(IncludedExtensions),
        ExcludedDirectories = ParseList(ExcludedDirectories),
        MaxFileSizeMB = MaxFileSizeMB,
        MaxConcurrentFiles = MaxConcurrentFiles
    };

    public static FileCrawlerProfileRecord FromDomain(FileCrawlerProfile profile) => new()
    {
        CrawlerId = profile.CrawlerId,
        RootPath = profile.RootPath,
        Recursive = profile.Recursive,
        UseAllSupportedFormats = profile.UseAllSupportedFormats,
        IncludedExtensions = profile.IncludedExtensions.Count > 0 ? string.Join(",", profile.IncludedExtensions) : null,
        ExcludedDirectories = profile.ExcludedDirectories.Count > 0 ? string.Join(",", profile.ExcludedDirectories) : null,
        MaxFileSizeMB = profile.MaxFileSizeMB,
        MaxConcurrentFiles = profile.MaxConcurrentFiles
    };

    private static IReadOnlyList<string> ParseList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
