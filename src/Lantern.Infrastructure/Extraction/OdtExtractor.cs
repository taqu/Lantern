using System.Text;
using System.Xml.Linq;
using Lantern.Core.Extraction;

namespace Lantern.Infrastructure.Extraction;

/// <summary>
/// Extracts text and structural metadata from <c>.odt</c> and <c>.ott</c>
/// OpenDocument Text packages. Produces one <see cref="ExtractedBlock"/> per
/// heading, paragraph, list, and table in document order.
/// </summary>
public sealed class OdtExtractor : IDocumentExtractor
{
    public bool CanExtract(DocFormat format) => format == DocFormat.Odt;

    public Task<ExtractedDocument> ExtractAsync(DocumentInput input, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Extract(input, cancellationToken), cancellationToken);
    }

    private static ExtractedDocument Extract(DocumentInput input, CancellationToken cancellationToken)
    {
        using OdfPackageReader package = OdfPackageReader.Open(input);
        if(package.Kind != OdfDocumentKind.Text)
            throw new NotSupportedException(
                $"Package is not an OpenDocument Text document (kind={package.Kind})");

        XDocument? content = package.LoadXml("content.xml", cancellationToken);
        OdfPackageMetadata meta = OdfPackageMetadata.Read(package, cancellationToken);

        List<ExtractedBlock> blocks = new List<ExtractedBlock>();
        if(content?.Root is not null) {
            XElement? body = content.Root.Element(OdfNamespaces.OfficeNs + "body");
            XElement? textBody = body?.Element(OdfNamespaces.OfficeNs + "text");
            if(textBody is not null)
                ExtractBlocks(textBody, blocks, currentSection: null, cancellationToken);
        }

        string title = !string.IsNullOrEmpty(meta.Title)
            ? meta.Title!
            : (input.FileName != null ? Path.GetFileNameWithoutExtension(input.FileName) : "Untitled");

        return new ExtractedDocument {
            Title = title,
            Blocks = blocks,
            Metadata = new ExtractedDocumentMetadata(
                Title: title,
                Author: meta.Author,
                CreatedAt: meta.CreatedAt,
                ModifiedAt: meta.ModifiedAt,
                Format: DocFormat.Odt)
        };
    }

    private static void ExtractBlocks(XElement container, List<ExtractedBlock> blocks,
        string? currentSection, CancellationToken cancellationToken)
    {
        foreach(XElement element in container.Elements()) {
            cancellationToken.ThrowIfCancellationRequested();
            if(element.Name.Namespace != OdfNamespaces.TextNs &&
                element.Name.Namespace != OdfNamespaces.TableNs)
                continue;

            string local = element.Name.LocalName;
            if(element.Name.Namespace == OdfNamespaces.TextNs) {
                switch(local) {
                    case "h": {
                        string text = ReadInlineText(element).Trim();
                        if(text.Length == 0) continue;
                        int level = ParseHeadingLevel(element);
                        text = TextNormalizer.Normalize(text);
                        if(level <= 2) currentSection = text;
                        blocks.Add(new ExtractedBlock(BlockType.Heading, text,
                            SectionTitle: currentSection, HeadingLevel: level));
                        break;
                    }
                    case "p": {
                        string text = ReadInlineText(element).Trim();
                        if(text.Length == 0) continue;
                        text = TextNormalizer.Normalize(text);
                        blocks.Add(new ExtractedBlock(BlockType.Paragraph, text,
                            SectionTitle: currentSection));
                        break;
                    }
                    case "list": {
                        string listText = ReadListText(element, level: 0);
                        if(!string.IsNullOrWhiteSpace(listText))
                            blocks.Add(new ExtractedBlock(BlockType.List,
                                TextNormalizer.NormalizeAll(listText),
                                SectionTitle: currentSection));
                        break;
                    }
                    case "section":
                        // Named sections group content — descend without emitting a block.
                        ExtractBlocks(element, blocks, currentSection, cancellationToken);
                        break;
                    default:
                        // Includes text:soft-page-break, bookmarks, etc. Descend for safety.
                        ExtractBlocks(element, blocks, currentSection, cancellationToken);
                        break;
                }
            } else if(element.Name == OdfNamespaces.TableNs + "table") {
                string tableText = ReadTableText(element);
                if(!string.IsNullOrWhiteSpace(tableText))
                    blocks.Add(new ExtractedBlock(BlockType.Table,
                        TextNormalizer.NormalizeAll(tableText),
                        SectionTitle: currentSection));
            }
        }
    }

    /// <summary>
    /// Reconstructs inline text for a paragraph or heading, honoring ODF's
    /// structural whitespace elements (<c>text:s</c>, <c>text:tab</c>,
    /// <c>text:line-break</c>) and ignoring non-text markup (fields, notes, etc.).
    /// </summary>
    private static string ReadInlineText(XElement element)
    {
        StringBuilder sb = new StringBuilder();
        AppendInline(element, sb);
        return sb.ToString();
    }

    private static void AppendInline(XElement element, StringBuilder sb)
    {
        foreach(XNode node in element.Nodes()) {
            if(node is XText t) {
                sb.Append(t.Value);
                continue;
            }
            if(node is not XElement child) continue;
            if(child.Name.Namespace == OdfNamespaces.TextNs) {
                switch(child.Name.LocalName) {
                    case "s": {
                        int count = 1;
                        XAttribute? c = child.Attribute(OdfNamespaces.TextNs + "c");
                        if(c != null && int.TryParse(c.Value, out int parsed) && parsed > 0)
                            count = Math.Min(parsed, 1024);
                        sb.Append(' ', count);
                        break;
                    }
                    case "tab":
                        sb.Append('\t');
                        break;
                    case "line-break":
                        sb.Append('\n');
                        break;
                    case "span":
                    case "a":
                        // Visible inline markup — descend to collect display text.
                        AppendInline(child, sb);
                        break;
                    case "note":
                        // Footnote/endnote content — skip here; it is collected
                        // as a trailing section outside this method if needed.
                        break;
                    case "tracked-changes":
                    case "change":
                    case "change-start":
                    case "change-end":
                        // Revision markers carry no visible text payload.
                        break;
                    default:
                        AppendInline(child, sb);
                        break;
                }
            } else {
                // Foreign namespace (draw, office) — descend to catch any text runs.
                AppendInline(child, sb);
            }
        }
    }

    private static int ParseHeadingLevel(XElement heading)
    {
        XAttribute? attr = heading.Attribute(OdfNamespaces.TextNs + "outline-level");
        if(attr != null && int.TryParse(attr.Value, out int level) && level > 0)
            return Math.Clamp(level, 1, 6);
        return 1;
    }

    private static string ReadListText(XElement list, int level)
    {
        StringBuilder sb = new StringBuilder();
        string indent = new string(' ', level * 2);
        foreach(XElement item in list.Elements(OdfNamespaces.TextNs + "list-item")) {
            foreach(XElement child in item.Elements()) {
                if(child.Name == OdfNamespaces.TextNs + "p" ||
                    child.Name == OdfNamespaces.TextNs + "h") {
                    string line = ReadInlineText(child).Trim();
                    if(line.Length > 0) sb.Append(indent).Append("- ").Append(line).Append('\n');
                } else if(child.Name == OdfNamespaces.TextNs + "list") {
                    sb.Append(ReadListText(child, level + 1));
                }
            }
        }
        return sb.ToString();
    }

    private static string ReadTableText(XElement table)
    {
        StringBuilder sb = new StringBuilder();
        foreach(XElement row in table.Descendants(OdfNamespaces.TableNs + "table-row")) {
            List<string> cells = new List<string>();
            foreach(XElement cell in row.Elements(OdfNamespaces.TableNs + "table-cell")) {
                StringBuilder cellBuilder = new StringBuilder();
                foreach(XElement p in cell.Elements()) {
                    if(p.Name == OdfNamespaces.TextNs + "p" ||
                        p.Name == OdfNamespaces.TextNs + "h") {
                        if(cellBuilder.Length > 0) cellBuilder.Append(' ');
                        cellBuilder.Append(ReadInlineText(p).Trim());
                    }
                }
                cells.Add(cellBuilder.ToString());
            }
            if(cells.Any(c => c.Length > 0))
                sb.AppendLine(string.Join(" | ", cells));
        }
        return sb.ToString().TrimEnd();
    }
}
