using System.Xml.Linq;

namespace Lantern.Infrastructure.Extraction;

/// <summary>
/// XML namespace URIs used by OpenDocument Format documents.
/// Centralized so every extractor parses with consistent namespace-aware names.
/// </summary>
public static class OdfNamespaces
{
    public const string Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    public const string Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    public const string Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    public const string Draw = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";
    public const string Presentation = "urn:oasis:names:tc:opendocument:xmlns:presentation:1.0";
    public const string Style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    public const string Meta = "urn:oasis:names:tc:opendocument:xmlns:meta:1.0";
    public const string Dc = "http://purl.org/dc/elements/1.1/";
    public const string Xlink = "http://www.w3.org/1999/xlink";
    public const string Manifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";
    public const string Svg = "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";
    public const string Fo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";

    public static readonly XNamespace OfficeNs = Office;
    public static readonly XNamespace TextNs = Text;
    public static readonly XNamespace TableNs = Table;
    public static readonly XNamespace DrawNs = Draw;
    public static readonly XNamespace PresentationNs = Presentation;
    public static readonly XNamespace StyleNs = Style;
    public static readonly XNamespace MetaNs = Meta;
    public static readonly XNamespace DcNs = Dc;
    public static readonly XNamespace XlinkNs = Xlink;
    public static readonly XNamespace ManifestNs = Manifest;
}
