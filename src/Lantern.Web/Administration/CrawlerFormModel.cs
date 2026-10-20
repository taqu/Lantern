using System.ComponentModel.DataAnnotations;
using Lantern.Core.Administration;

namespace Lantern.Web.Administration;

public sealed class CrawlerFormModel
{
    [Required(ErrorMessage = "Crawler name is required.")]
    [StringLength(120, MinimumLength = 1, ErrorMessage = "Name must be 1–120 characters.")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Root directory is required.")]
    public string RootPath { get; set; } = "";

    public bool Enabled { get; set; } = true;
    public bool Recursive { get; set; } = true;
    public bool UseAllSupportedFormats { get; set; } = true;

    public string IncludedExtensionsRaw { get; set; } = "";
    public string ExcludedDirectoriesRaw { get; set; } = ".git,node_modules,bin,obj,.vs";

    [Range(1, 10000, ErrorMessage = "Maximum file size must be between 1 and 10000 MB.")]
    public int MaxFileSizeMB { get; set; } = 100;

    [Range(1, 32, ErrorMessage = "Maximum concurrent files must be between 1 and 32.")]
    public int MaxConcurrentFiles { get; set; } = 2;

    public FileCrawlerProfile ToProfile(Guid crawlerId = default) => new()
    {
        CrawlerId = crawlerId,
        RootPath = RootPath.Trim(),
        Recursive = Recursive,
        UseAllSupportedFormats = UseAllSupportedFormats,
        IncludedExtensions = ParseExtensions(),
        ExcludedDirectories = ParseList(ExcludedDirectoriesRaw),
        MaxFileSizeMB = MaxFileSizeMB,
        MaxConcurrentFiles = MaxConcurrentFiles
    };

    public IReadOnlyList<string> ParseExtensions() =>
        ParseList(IncludedExtensionsRaw)
            .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
            .Distinct()
            .ToList();

    private static List<string> ParseList(string raw) =>
        raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
           .Where(s => s.Length > 0)
           .ToList();

    public static CrawlerFormModel From(CrawlerDefinition definition, FileCrawlerProfile profile) => new()
    {
        Name = definition.Name,
        Enabled = definition.Enabled,
        RootPath = profile.RootPath,
        Recursive = profile.Recursive,
        UseAllSupportedFormats = profile.UseAllSupportedFormats,
        IncludedExtensionsRaw = string.Join(", ", profile.IncludedExtensions),
        ExcludedDirectoriesRaw = string.Join(", ", profile.ExcludedDirectories),
        MaxFileSizeMB = profile.MaxFileSizeMB,
        MaxConcurrentFiles = profile.MaxConcurrentFiles
    };
}
