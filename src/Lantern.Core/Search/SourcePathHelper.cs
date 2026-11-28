namespace Lantern.Core.Search;

public static class SourcePathHelper
{
    public static bool IsWebResult(SearchPageItem item) =>
        string.Equals(item.SourceType, "web", StringComparison.OrdinalIgnoreCase);

    public static string? GetCopyPath(SearchPageItem item)
    {
        if (IsWebResult(item))
            return string.IsNullOrEmpty(item.Url) ? null : item.Url;

        if (!string.IsNullOrEmpty(item.Path))
            return item.Path;

        return DecodeFileUri(item.Url);
    }

    public static string? GetCopyDirectory(SearchPageItem item)
    {
        if (IsWebResult(item)) return null;
        return GetParentDirectory(GetCopyPath(item));
    }

    public static string? GetParentDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        // UNC: \\server\share[\rest...]
        if (path.StartsWith("\\\\", StringComparison.Ordinal))
        {
            string[] parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 2) return null;
            return "\\\\" + string.Join("\\", parts[..^1]);
        }

        // Windows absolute: X:\[rest...]
        if (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\')
        {
            if (path.Length == 3) return null;
            int lastSep = path.LastIndexOf('\\');
            return lastSep <= 2 ? path[..3] : path[..lastSep];
        }

        // Linux POSIX: /[rest...]
        if (path.StartsWith('/'))
        {
            if (path == "/") return null;
            int lastSep = path.LastIndexOf('/');
            return lastSep == 0 ? "/" : path[..lastSep];
        }

        return null;
    }

    // Decodes a file: URI back to its original platform path without using
    // System.Uri.LocalPath, which applies host-OS path conventions.
    public static string? DecodeFileUri(string? uri)
    {
        if (string.IsNullOrEmpty(uri)) return null;

        if (uri.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
        {
            string decoded = Uri.UnescapeDataString(uri["file:///".Length..]);
            // Windows drive path: starts with letter + ':'
            if (decoded.Length >= 2 && char.IsAsciiLetter(decoded[0]) && decoded[1] == ':')
                return decoded.Replace('/', '\\');
            // Linux absolute path
            return "/" + decoded.TrimStart('/');
        }

        // UNC: file://server/share/[rest]
        if (uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            string decoded = Uri.UnescapeDataString(uri["file://".Length..]);
            return "\\\\" + decoded.Replace('/', '\\').TrimEnd('\\');
        }

        return null;
    }
}
