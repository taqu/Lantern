using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lantern.Core.Search;

public static class DocumentIdentity
{
    public static string FilePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        path = path.Replace('/', '\\');
        if (!(path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\') &&
            !path.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("An absolute drive or UNC path is required.", nameof(path));
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
            throw new ArgumentException("Device paths are not supported.", nameof(path));
        string result = System.IO.Path.GetFullPath(path);
        if (result.StartsWith(@"\\", StringComparison.Ordinal) && result.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length < 2)
            throw new ArgumentException("A UNC share is required.", nameof(path));
        return System.IO.Path.TrimEndingDirectorySeparator(result);
    }

    public static string WebUri(string location)
    {
        if (!System.Uri.TryCreate(location, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("An absolute HTTP(S) URL without credentials is required.", nameof(location));
        return new UriBuilder(uri) { Fragment = "" }.Uri.AbsoluteUri;
    }

    public static string Generate(string sourceType, string sourceId, string location)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        string canonical = sourceType switch
        {
            "file" => FilePath(location).ToUpperInvariant(),
            "web" => WebUri(location),
            _ => throw new ArgumentException("Source type must be file or web.", nameof(sourceType))
        };
        return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] { sourceType, sourceId, canonical })));
    }
}

public static class DocumentNormalizer
{
    public static SearchDocument Normalize(SearchDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.SourceId);
        ArgumentNullException.ThrowIfNull(document.Content);
        if (document.SourceId != document.SourceId.Trim() || document.SourceId.Any(char.IsControl))
            throw new ArgumentException("Source identity must not contain control or surrounding whitespace.");
        if (document.Size < 0) throw new ArgumentException("Size cannot be negative.");
        string? path = document.SourceType == "file" ? DocumentIdentity.FilePath(document.Path ?? document.SourcePath ?? "") : null;
        if (document.SourceType == "web" && document.Path != null) throw new ArgumentException("Web documents cannot have a file path.");
        string uri = path != null ? new Uri(path).AbsoluteUri : DocumentIdentity.WebUri(document.Uri);
        string id = DocumentIdentity.Generate(document.SourceType, document.SourceId, path ?? uri);
        if (document.Id.Length != 0 && document.Id != id) throw new ArgumentException("Document ID does not match its source identity.");
        string title = Clean(document.Title).Trim();
        if (title.Length == 0) throw new ArgumentException("Title must contain visible text.");
        string? extension = document.Extension?.Trim() ?? (path != null ? System.IO.Path.GetExtension(path) : null);
        if (!string.IsNullOrEmpty(extension)) extension = "." + extension.TrimStart('.').ToLowerInvariant();
        return new SearchDocument
        {
            Id = id, SourceId = document.SourceId, SourceType = document.SourceType,
            Title = title, Content = Clean(document.Content), Uri = uri, Path = path,
            Extension = string.IsNullOrEmpty(extension) ? null : extension,
            MimeType = string.IsNullOrWhiteSpace(document.MimeType) ? null : document.MimeType.Trim().ToLowerInvariant(),
            Size = document.Size, ModifiedAt = document.ModifiedAt?.ToUniversalTime(), IndexedAt = document.IndexedAt.ToUniversalTime(),
            CategoryIds = document.CategoryIds
        };
    }

    private static string Clean(string text)
    {
        if (!text.Any(c => c == '\r' || (char.IsControl(c) && c != '\n' && c != '\t'))) return text;
        var builder = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r') { builder.Append('\n'); if (i + 1 < text.Length && text[i + 1] == '\n') i++; }
            else if (!char.IsControl(c) || c is '\n' or '\t') builder.Append(c);
        }
        return builder.ToString();
    }
}
