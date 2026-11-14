using System.Globalization;
using System.Xml.Linq;

namespace Lantern.Infrastructure.Extraction;

/// <summary>
/// Reads the <c>meta.xml</c> entry of an ODF package and exposes the handful of
/// fields Lantern surfaces (title, author, created, modified).
/// </summary>
internal readonly struct OdfPackageMetadata
{
    public string? Title { get; init; }
    public string? Author { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? ModifiedAt { get; init; }

    public static OdfPackageMetadata Read(OdfPackageReader package, CancellationToken cancellationToken)
    {
        XDocument? doc = null;
        try {
            doc = package.LoadXml("meta.xml", cancellationToken);
        } catch(OdfPackageException) {
            // Metadata is best-effort; a malformed meta.xml must not abort extraction.
        }
        if(doc?.Root is null) return default;

        XElement? meta = doc.Root.Element(OdfNamespaces.OfficeNs + "meta");
        if(meta is null) return default;

        string? title = meta.Element(OdfNamespaces.DcNs + "title")?.Value?.Trim();
        string? author = meta.Element(OdfNamespaces.MetaNs + "initial-creator")?.Value?.Trim()
            ?? meta.Element(OdfNamespaces.DcNs + "creator")?.Value?.Trim();
        DateTimeOffset? created = ParseDate(meta.Element(OdfNamespaces.MetaNs + "creation-date")?.Value);
        DateTimeOffset? modified = ParseDate(meta.Element(OdfNamespaces.DcNs + "date")?.Value);

        return new OdfPackageMetadata {
            Title = string.IsNullOrEmpty(title) ? null : title,
            Author = string.IsNullOrEmpty(author) ? null : author,
            CreatedAt = created,
            ModifiedAt = modified,
        };
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if(string.IsNullOrWhiteSpace(value)) return null;
        if(DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset dto))
            return dto;
        return null;
    }
}
