using Lantern.Core.Crawling;

namespace Lantern.Crawling.Files;

public sealed class FileFilter
{
    private readonly HashSet<string> _includedExtensions;
    private readonly HashSet<string> _excludedDirectoryNames;
    private readonly long _maxBytes;
    public bool Recursive { get; }

    public FileFilter(FileCrawlerConfiguration config)
    {
        Recursive = config.Recursive;
        _maxBytes = (long)config.MaxFileSizeMB * 1024 * 1024;
        _includedExtensions = config.IncludedExtensions
            .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _excludedDirectoryNames = config.ExcludedDirectories
            .Select(d => d.Trim().TrimEnd('/', '\\'))
            .Where(d => d.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool ShouldIncludeFile(FileInfo info)
    {
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
        if (_includedExtensions.Count > 0 && !_includedExtensions.Contains(info.Extension)) return false;
        if (_maxBytes > 0 && info.Length > _maxBytes) return false;
        return true;
    }

    public bool ShouldExcludeDirectory(DirectoryInfo dir)
    {
        if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) return true;
        string name = dir.Name;
        return _excludedDirectoryNames.Contains(name);
    }
}
