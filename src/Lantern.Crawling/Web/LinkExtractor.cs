using HtmlAgilityPack;
using System.Net;
using System.Text;

namespace Lantern.Crawling.Web;

/// <summary>
/// Extracts hyperlinks from HTML content, resolving relative URLs against the base.
/// Does not execute JavaScript. Respects HTML base elements where safe.
/// </summary>
public static class LinkExtractor
{
    public static IReadOnlyList<string> ExtractLinks(byte[] htmlBytes, string pageUrl)
    {
        string html;
        try { html = DecodeHtml(htmlBytes); }
        catch { return []; }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Determine effective base URL
        string baseUrl = pageUrl;
        HtmlNode? baseTag = doc.DocumentNode.SelectSingleNode("//head/base[@href]")
                         ?? doc.DocumentNode.SelectSingleNode("//base[@href]");
        if (baseTag != null)
        {
            string? baseHref = baseTag.GetAttributeValue("href", null);
            if (!string.IsNullOrWhiteSpace(baseHref))
            {
                string? normalized = UrlNormalizer.TryNormalize(baseHref, pageUrl);
                // Only use base tag if it stays on same origin (security: don't redirect all links to external)
                if (normalized != null && SameOrigin(normalized, pageUrl))
                    baseUrl = normalized;
            }
        }

        var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Extract <a href>
        HtmlNodeCollection? anchors = doc.DocumentNode.SelectNodes("//a[@href]");
        if (anchors != null)
        {
            foreach (HtmlNode a in anchors)
            {
                string href = WebUtility.HtmlDecode(a.GetAttributeValue("href", "")).Trim();
                if (string.IsNullOrEmpty(href) || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                    || href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
                    || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
                    continue;
                string? norm = UrlNormalizer.TryNormalize(href, baseUrl);
                if (norm != null) links.Add(norm);
            }
        }

        return [.. links];
    }

    public static string? ExtractCanonicalUrl(byte[] htmlBytes, string pageUrl)
    {
        string html;
        try { html = DecodeHtml(htmlBytes); }
        catch { return null; }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        HtmlNode? canonical = doc.DocumentNode.SelectSingleNode("//link[@rel='canonical' and @href]");
        if (canonical == null) return null;

        string href = WebUtility.HtmlDecode(canonical.GetAttributeValue("href", "")).Trim();
        return UrlNormalizer.TryNormalize(href, pageUrl);
    }

    private static bool SameOrigin(string a, string b)
    {
        UrlNormalizer.TryGetOrigin(a, out string oa);
        UrlNormalizer.TryGetOrigin(b, out string ob);
        return string.Equals(oa, ob, StringComparison.OrdinalIgnoreCase);
    }

    private static string DecodeHtml(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        return Encoding.UTF8.GetString(bytes);
    }
}
