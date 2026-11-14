using System.Text;
using System.Xml.Linq;
using Lantern.Core.Extraction;

namespace Lantern.Infrastructure.Extraction;

/// <summary>
/// Extracts slide text, shape text, and speaker notes from <c>.odp</c> and
/// <c>.otp</c> OpenDocument Presentation packages. Produces one heading block
/// per slide title, one paragraph block per slide body, and (when present) a
/// separate notes block with a distinguishing section title.
/// </summary>
public sealed class OdpExtractor : IDocumentExtractor
{
    public bool CanExtract(DocFormat format) => format == DocFormat.Odp;

    public Task<ExtractedDocument> ExtractAsync(DocumentInput input, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Extract(input, cancellationToken), cancellationToken);
    }

    private static ExtractedDocument Extract(DocumentInput input, CancellationToken cancellationToken)
    {
        using OdfPackageReader package = OdfPackageReader.Open(input);
        if(package.Kind != OdfDocumentKind.Presentation)
            throw new NotSupportedException(
                $"Package is not an OpenDocument Presentation (kind={package.Kind})");

        XDocument? content = package.LoadXml("content.xml", cancellationToken);
        OdfPackageMetadata meta = OdfPackageMetadata.Read(package, cancellationToken);

        List<ExtractedBlock> blocks = new List<ExtractedBlock>();
        int slideCount = 0;

        if(content?.Root is not null) {
            XElement? body = content.Root.Element(OdfNamespaces.OfficeNs + "body");
            XElement? presentation = body?.Element(OdfNamespaces.OfficeNs + "presentation");
            if(presentation is not null) {
                int slideNumber = 0;
                foreach(XElement page in presentation.Elements(OdfNamespaces.DrawNs + "page")) {
                    cancellationToken.ThrowIfCancellationRequested();
                    slideNumber++;
                    slideCount++;
                    EmitSlide(page, slideNumber, blocks);
                }
            }
        }

        string title = !string.IsNullOrEmpty(meta.Title)
            ? meta.Title!
            : (input.FileName != null ? Path.GetFileNameWithoutExtension(input.FileName) : "Presentation");

        return new ExtractedDocument {
            Title = title,
            Blocks = blocks,
            Metadata = new ExtractedDocumentMetadata(
                Title: title,
                Author: meta.Author,
                CreatedAt: meta.CreatedAt,
                ModifiedAt: meta.ModifiedAt,
                Format: DocFormat.Odp,
                SlideCount: slideCount)
        };
    }

    private static void EmitSlide(XElement page, int slideNumber, List<ExtractedBlock> blocks)
    {
        string? pageName = page.Attribute(OdfNamespaces.DrawNs + "name")?.Value;
        if(string.IsNullOrWhiteSpace(pageName)) pageName = null;

        // Collect all slide-level frame text, excluding the presentation:notes
        // subtree which is emitted separately.
        List<string> slideTexts = new List<string>();
        string? slideTitle = null;

        foreach(XElement frame in page.Elements(OdfNamespaces.DrawNs + "frame")) {
            string? presentationClass = frame.Attribute(OdfNamespaces.PresentationNs + "class")?.Value;
            string frameText = CollectFrameText(frame);
            if(string.IsNullOrWhiteSpace(frameText)) continue;
            if(presentationClass == "title" && slideTitle == null)
                slideTitle = frameText.Trim();
            else
                slideTexts.Add(frameText.Trim());
        }

        // Some slides use <draw:text-box> siblings outside a frame — catch them.
        foreach(XElement box in page.Descendants(OdfNamespaces.DrawNs + "text-box")) {
            if(box.Ancestors(OdfNamespaces.DrawNs + "frame").Any()) continue;
            if(box.Ancestors(OdfNamespaces.PresentationNs + "notes").Any()) continue;
            string text = CollectTextBoxText(box);
            if(!string.IsNullOrWhiteSpace(text)) slideTexts.Add(text.Trim());
        }

        string sectionLabel = slideTitle ?? pageName ?? $"Slide {slideNumber}";

        if(!string.IsNullOrWhiteSpace(slideTitle))
            blocks.Add(new ExtractedBlock(BlockType.Heading,
                TextNormalizer.Normalize(slideTitle!),
                SlideNumber: slideNumber, SectionTitle: sectionLabel, HeadingLevel: 2));

        if(slideTexts.Count > 0) {
            string joined = string.Join('\n', slideTexts);
            blocks.Add(new ExtractedBlock(BlockType.Paragraph,
                TextNormalizer.NormalizeAll(joined),
                SlideNumber: slideNumber, SectionTitle: sectionLabel));
        }

        // Speaker notes — section title carries the slide label plus a
        // disambiguating "Notes" marker so the chunker can treat them as a
        // distinct unit and search results can tell them apart.
        XElement? notes = page.Element(OdfNamespaces.PresentationNs + "notes");
        if(notes is not null) {
            string notesText = CollectFrameText(notes);
            if(!string.IsNullOrWhiteSpace(notesText))
                blocks.Add(new ExtractedBlock(BlockType.Paragraph,
                    TextNormalizer.NormalizeAll(notesText),
                    SlideNumber: slideNumber,
                    SectionTitle: $"{sectionLabel} — Notes"));
        }
    }

    private static string CollectFrameText(XElement container)
    {
        StringBuilder sb = new StringBuilder();
        foreach(XElement box in container.Descendants(OdfNamespaces.DrawNs + "text-box")) {
            string text = CollectTextBoxText(box);
            if(text.Length == 0) continue;
            if(sb.Length > 0) sb.Append('\n');
            sb.Append(text);
        }
        return sb.ToString();
    }

    private static string CollectTextBoxText(XElement textBox)
    {
        StringBuilder sb = new StringBuilder();
        foreach(XElement para in textBox.Elements()) {
            if(para.Name != OdfNamespaces.TextNs + "p" &&
                para.Name != OdfNamespaces.TextNs + "h" &&
                para.Name != OdfNamespaces.TextNs + "list")
                continue;

            if(para.Name == OdfNamespaces.TextNs + "list") {
                foreach(XElement item in para.Elements(OdfNamespaces.TextNs + "list-item")) {
                    foreach(XElement sub in item.Elements()) {
                        if(sub.Name == OdfNamespaces.TextNs + "p" ||
                            sub.Name == OdfNamespaces.TextNs + "h") {
                            string line = ReadInline(sub).Trim();
                            if(line.Length > 0) {
                                if(sb.Length > 0) sb.Append('\n');
                                sb.Append("- ").Append(line);
                            }
                        }
                    }
                }
                continue;
            }

            string text = ReadInline(para).Trim();
            if(text.Length == 0) continue;
            if(sb.Length > 0) sb.Append('\n');
            sb.Append(text);
        }
        return sb.ToString();
    }

    private static string ReadInline(XElement element)
    {
        StringBuilder sb = new StringBuilder();
        Append(element, sb);
        return sb.ToString();
    }

    private static void Append(XElement element, StringBuilder sb)
    {
        foreach(XNode node in element.Nodes()) {
            if(node is XText t) { sb.Append(t.Value); continue; }
            if(node is not XElement child) continue;
            if(child.Name.Namespace == OdfNamespaces.TextNs) {
                switch(child.Name.LocalName) {
                    case "s": {
                        int count = 1;
                        XAttribute? c = child.Attribute(OdfNamespaces.TextNs + "c");
                        if(c != null && int.TryParse(c.Value, out int parsed) && parsed > 0)
                            count = Math.Min(parsed, 128);
                        sb.Append(' ', count);
                        break;
                    }
                    case "tab":
                        sb.Append('\t');
                        break;
                    case "line-break":
                        sb.Append('\n');
                        break;
                    default:
                        Append(child, sb);
                        break;
                }
            } else {
                Append(child, sb);
            }
        }
    }
}
