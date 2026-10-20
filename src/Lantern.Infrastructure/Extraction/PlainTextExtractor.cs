using System.Text;
using Lantern.Core.Extraction;

namespace Lantern.Infrastructure.Extraction;

public sealed class PlainTextExtractor : IDocumentExtractor
{
    public bool CanExtract(DocFormat format) =>
        format == DocFormat.PlainText;

    public async Task<ExtractedDocument> ExtractAsync(DocumentInput input, CancellationToken cancellationToken = default)
    {
        string text = await ReadTextAsync(input, cancellationToken);
        text = TextNormalizer.NormalizeAll(text);

        string title = ExtractTitle(input, text);
        IReadOnlyList<ExtractedBlock> blocks = SplitIntoBlocks(text);

        return new ExtractedDocument
        {
            Title = title,
            Blocks = blocks,
            Metadata = new ExtractedDocumentMetadata(Title: title, Format: DocFormat.PlainText)
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
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3); // UTF-8 BOM
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2); // UTF-16 LE BOM
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2); // UTF-16 BE BOM
        // Default: try UTF-8, fall back gracefully
        try { return Encoding.UTF8.GetString(bytes); }
        catch { return Encoding.Default.GetString(bytes); }
    }

    private static string ExtractTitle(DocumentInput input, string text)
    {
        if (input.FileName != null)
            return Path.GetFileNameWithoutExtension(input.FileName);
        // Use first non-empty line as title
        string? firstLine = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return firstLine?.Length > 80 ? firstLine[..80] : firstLine ?? "Untitled";
    }

    private static IReadOnlyList<ExtractedBlock> SplitIntoBlocks(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var blocks = new List<ExtractedBlock>();
        string[] paragraphs = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int offset = 0;
        foreach (string para in paragraphs)
        {
            if (!string.IsNullOrWhiteSpace(para))
                blocks.Add(new ExtractedBlock(BlockType.PlainText, para.Trim(), StartOffset: offset, EndOffset: offset + para.Length));
            offset += para.Length + 2;
        }
        return blocks;
    }
}
