namespace Lantern.Core.Search;

/// <summary>
/// Provides URL safety checks for display in the search UI.
/// </summary>
public static class UrlValidator
{
    /// <summary>
    /// Returns true if the URL is safe to render as a clickable link
    /// (i.e. it starts with http:// or https://).
    /// </summary>
    public static bool IsSafeDisplayUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        return url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
    }
}
