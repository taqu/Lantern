namespace Lantern.Crawling.Web;

/// <summary>
/// Normalizes HTTP/HTTPS URLs to a canonical form for deduplication and identity.
/// Preserves path case, meaningful query parameters, and percent-encoding.
/// Removes fragments. Normalizes scheme/host to lowercase. Removes default ports.
/// </summary>
public static class UrlNormalizer
{
    public static string? TryNormalize(string? raw, string? baseUrl = null)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        raw = raw.Trim();

        // Handle protocol-relative URLs
        if (raw.StartsWith("//", StringComparison.Ordinal))
        {
            if (baseUrl != null && Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? b))
                raw = b.Scheme + ":" + raw;
            else
                raw = "https:" + raw;
        }

        Uri? uri;
        if (Uri.TryCreate(raw, UriKind.Absolute, out uri))
        {
            // Already absolute — check scheme
        }
        else if (baseUrl != null && Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? b2))
        {
            // Relative URL — resolve against base
            if (!Uri.TryCreate(b2, raw, out uri)) return null;
        }
        else return null;

        if (uri.Scheme != "http" && uri.Scheme != "https") return null;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return null;

        // Normalize: lowercase scheme and host, remove fragment, remove default port
        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.Host.ToLowerInvariant(),
            Fragment = "" // remove fragment
        };

        // Remove default ports
        if ((uri.Scheme == "http" && uri.Port == 80) || (uri.Scheme == "https" && uri.Port == 443))
            builder.Port = -1;

        try
        {
            return builder.Uri.AbsoluteUri;
        }
        catch
        {
            return null;
        }
    }

    public static bool TryGetOrigin(string url, out string origin)
    {
        origin = "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? u)) return false;
        origin = $"{u.Scheme.ToLowerInvariant()}://{u.Host.ToLowerInvariant()}" +
                 (IsDefaultPort(u) ? "" : $":{u.Port}");
        return true;
    }

    public static string? GetHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? u)) return null;
        return u.Host.ToLowerInvariant();
    }

    private static bool IsDefaultPort(Uri u) =>
        (u.Scheme == "http" && u.Port == 80) || (u.Scheme == "https" && u.Port == 443) || u.Port == -1;
}
