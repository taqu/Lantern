using System.Text;
using Lantern.Core.Extraction;

namespace Lantern.Infrastructure.Extraction;

public sealed class MarkdownExtractor : IDocumentExtractor
{
    public bool CanExtract(DocFormat format) => format == DocFormat.Markdown;

    public async Task<ExtractedDocument> ExtractAsync(DocumentInput input, CancellationToken cancellationToken = default)
    {
        string rawText = await ReadTextAsync(input, cancellationToken);
        rawText = TextNormalizer.NormalizeAll(rawText);

        string title = ExtractMarkdownTitle(input, rawText);
        IReadOnlyList<ExtractedBlock> blocks = ParseMarkdownBlocks(rawText);

        return new ExtractedDocument
        {
            Title = title,
            Blocks = blocks,
            Metadata = new ExtractedDocumentMetadata(Title: title, Format: DocFormat.Markdown)
        };
    }

    private static async Task<string> ReadTextAsync(DocumentInput input, CancellationToken ct)
    {
        if (input.FilePath != null)
        {
            byte[] bytes = await File.ReadAllBytesAsync(input.FilePath, ct);
            return DetectAndDecode(bytes);
        }
        if (input.Stream != null)
        {
            using var ms = new MemoryStream();
            await input.Stream.CopyToAsync(ms, ct);
            return DetectAndDecode(ms.ToArray());
        }
        return string.Empty;
    }

    private static string DetectAndDecode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        try { return Encoding.UTF8.GetString(bytes); }
        catch { return Encoding.Default.GetString(bytes); }
    }

    private static string ExtractMarkdownTitle(DocumentInput input, string text)
    {
        // Try to find first H1
        string[] lines = text.Split('\n');
        foreach (string line in lines)
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("# "))
                return trimmed[2..].Trim();
        }
        return input.FileName != null ? Path.GetFileNameWithoutExtension(input.FileName) : "Untitled";
    }

    private static IReadOnlyList<ExtractedBlock> ParseMarkdownBlocks(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var blocks = new List<ExtractedBlock>();
        string[] lines = text.Split('\n');
        string? currentSection = null;
        var currentParagraph = new StringBuilder();
        int currentOffset = 0;
        int offset = 0;

        void FlushParagraph()
        {
            string content = currentParagraph.ToString().Trim();
            if (!string.IsNullOrEmpty(content))
                blocks.Add(new ExtractedBlock(BlockType.Paragraph, content, SectionTitle: currentSection, StartOffset: currentOffset, EndOffset: currentOffset + content.Length));
            currentParagraph.Clear();
        }

        foreach (string line in lines)
        {
            string trimmed = line.TrimStart();

            if (trimmed.StartsWith('#'))
            {
                FlushParagraph();
                int level = 0;
                while (level < trimmed.Length && trimmed[level] == '#') level++;
                string headingText = trimmed[level..].Trim();
                if (!string.IsNullOrEmpty(headingText))
                {
                    if (level == 1 || level == 2) currentSection = headingText;
                    blocks.Add(new ExtractedBlock(BlockType.Heading, headingText, SectionTitle: currentSection, HeadingLevel: level, StartOffset: offset, EndOffset: offset + line.Length));
                }
            }
            else if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                currentOffset = offset + line.Length + 1;
            }
            else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ") ||
                     (trimmed.Length > 2 && char.IsDigit(trimmed[0]) && trimmed[1] == '.'))
            {
                // List item - add to current paragraph
                if (currentParagraph.Length == 0) currentOffset = offset;
                if (currentParagraph.Length > 0) currentParagraph.Append('\n');
                currentParagraph.Append(trimmed.TrimStart('-', '*', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.', ' '));
            }
            else if (trimmed.StartsWith("```"))
            {
                // Code fence delimiter - include first line content if any
                if (currentParagraph.Length == 0) currentOffset = offset;
                string codeContent = trimmed[3..].Trim();
                if (!string.IsNullOrEmpty(codeContent))
                {
                    if (currentParagraph.Length > 0) currentParagraph.Append('\n');
                    currentParagraph.Append(codeContent);
                }
            }
            else
            {
                if (currentParagraph.Length == 0) currentOffset = offset;
                if (currentParagraph.Length > 0) currentParagraph.Append(' ');
                currentParagraph.Append(trimmed);
            }

            offset += line.Length + 1; // +1 for \n
        }

        FlushParagraph();
        return blocks;
    }
}
