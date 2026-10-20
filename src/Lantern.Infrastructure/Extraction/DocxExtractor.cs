using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Lantern.Core.Extraction;
using System.Text;

namespace Lantern.Infrastructure.Extraction;

public sealed class DocxExtractor : IDocumentExtractor
{
    public bool CanExtract(DocFormat format) => format == DocFormat.Docx;

    public Task<ExtractedDocument> ExtractAsync(DocumentInput input, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Extract(input), cancellationToken);
    }

    private static ExtractedDocument Extract(DocumentInput input)
    {
        Stream stream;
        bool ownsStream = false;

        if (input.FilePath != null)
        {
            stream = File.OpenRead(input.FilePath);
            ownsStream = true;
        }
        else if (input.Stream != null)
            stream = input.Stream;
        else
            throw new ArgumentException("No source in DocumentInput");

        try
        {
            using var doc = WordprocessingDocument.Open(stream, isEditable: false);
            Body? body = doc.MainDocumentPart?.Document?.Body;
            if (body == null)
                return EmptyDocument(input, DocFormat.Docx);

            var blocks = new List<ExtractedBlock>();
            string? currentSection = null;

            // Get package properties safely
            string? author = null;
            DateTimeOffset? created = null;
            DateTimeOffset? modified = null;
            string? docTitle = null;
            try
            {
                IPackageProperties? props = doc.PackageProperties;
                author = props?.Creator;
                docTitle = props?.Title?.Trim();
                if (props?.Created is DateTime createdDt) created = new DateTimeOffset(createdDt);
                if (props?.Modified is DateTime modifiedDt) modified = new DateTimeOffset(modifiedDt);
            }
            catch { /* Ignore properties errors */ }

            foreach (DocumentFormat.OpenXml.OpenXmlElement element in body.ChildElements)
            {
                if (element is Paragraph para)
                {
                    string text = GetParagraphText(para);
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    text = TextNormalizer.Normalize(text.Trim());
                    string styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
                    bool isHeading = styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) ||
                                     styleId.StartsWith("heading", StringComparison.OrdinalIgnoreCase);

                    if (isHeading)
                    {
                        int level = ExtractHeadingLevel(styleId);
                        if (level <= 2) currentSection = text;
                        blocks.Add(new ExtractedBlock(BlockType.Heading, text, SectionTitle: currentSection, HeadingLevel: level));
                    }
                    else
                    {
                        blocks.Add(new ExtractedBlock(BlockType.Paragraph, text, SectionTitle: currentSection));
                    }
                }
                else if (element is Table table)
                {
                    string tableText = ExtractTableText(table);
                    if (!string.IsNullOrEmpty(tableText))
                        blocks.Add(new ExtractedBlock(BlockType.Table, tableText, SectionTitle: currentSection));
                }
            }

            string title = !string.IsNullOrEmpty(docTitle)
                ? docTitle
                : (input.FileName != null ? Path.GetFileNameWithoutExtension(input.FileName) : "Untitled");

            return new ExtractedDocument
            {
                Title = title,
                Blocks = blocks,
                Metadata = new ExtractedDocumentMetadata(
                    Title: title,
                    Author: author,
                    CreatedAt: created,
                    ModifiedAt: modified,
                    Format: DocFormat.Docx)
            };
        }
        finally
        {
            if (ownsStream) stream.Dispose();
        }
    }

    private static string GetParagraphText(Paragraph para)
    {
        var sb = new StringBuilder();
        foreach (Run run in para.Descendants<Run>())
        {
            foreach (Text text in run.Descendants<Text>())
                sb.Append(text.Text);
        }
        return sb.ToString();
    }

    private static string ExtractTableText(Table table)
    {
        var sb = new StringBuilder();
        foreach (TableRow row in table.Descendants<TableRow>())
        {
            IEnumerable<string> cells = row.Descendants<TableCell>().Select(c => c.InnerText.Trim());
            sb.AppendLine(string.Join(" | ", cells));
        }
        return sb.ToString().Trim();
    }

    private static int ExtractHeadingLevel(string styleId)
    {
        for (int i = 1; i <= 6; i++)
            if (styleId.EndsWith(i.ToString())) return i;
        return 1;
    }

    private static ExtractedDocument EmptyDocument(DocumentInput input, DocFormat format) =>
        new()
        {
            Title = input.FileName != null ? Path.GetFileNameWithoutExtension(input.FileName) : "Untitled",
            Blocks = [],
            Metadata = new ExtractedDocumentMetadata(Format: format)
        };
}
