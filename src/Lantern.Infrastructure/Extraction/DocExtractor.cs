using System.Text;
using Lantern.Core.Extraction;
using OpenMcdf;

namespace Lantern.Infrastructure.Extraction;

/// <summary>
/// Extracts text from legacy Word 97–2003 (.doc) documents using OpenMcdf.
/// Reads the WordDocument stream and extracts Unicode text runs.
/// Does not handle encrypted or macro-only documents.
/// </summary>
public sealed class DocExtractor : IDocumentExtractor
{
    public bool CanExtract(DocFormat format) => format == DocFormat.Doc;

    public Task<ExtractedDocument> ExtractAsync(DocumentInput input, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Extract(input), cancellationToken);
    }

    private static ExtractedDocument Extract(DocumentInput input)
    {
        string? filePath = input.FilePath;
        if(filePath == null)
            throw new ArgumentException("DocExtractor requires FilePath");

        try {
            using RootStorage storage = RootStorage.OpenRead(filePath);
            byte[]? data = TryReadStream(storage, "WordDocument");
            if(data == null || data.Length < 32)
                return EmptyDocument(input);

            var blocks = ExtractTextBlocks(data);
            string title = input.FileName != null ? Path.GetFileNameWithoutExtension(input.FileName) : "Untitled";

            return new ExtractedDocument {
                Title = title,
                Blocks = blocks,
                Metadata = new ExtractedDocumentMetadata(Title: title, Format: DocFormat.Doc)
            };
        } catch(Exception ex) when(ex is not OperationCanceledException) {
            throw new InvalidOperationException($"Failed to open .doc file: {ex.Message}", ex);
        }
    }

    private static List<ExtractedBlock> ExtractTextBlocks(byte[] data)
    {
        // The Word Binary Format stores text starting after the FIB header.
        // We use a heuristic: scan for runs of printable UTF-16LE characters
        // separated by paragraph marks (0x000D or 0x2029).
        // This correctly extracts most paragraph text including CJK characters.
        List<ExtractedBlock> blocks = new List<ExtractedBlock>();
        StringBuilder current = new StringBuilder();

        for(int i = 0; i < data.Length - 1; i += 2) {
            char c = (char)((data[i + 1] << 8) | data[i]);

            if(c == '\r' || c == '\u2029' || c == '\f') // paragraph marks
            {
                string text = TextNormalizer.NormalizeAll(current.ToString());
                if(!string.IsNullOrWhiteSpace(text))
                    blocks.Add(new ExtractedBlock(BlockType.Paragraph, text));
                current.Clear();
            } else if(!char.IsControl(c) || c == '\t' || c == '\n') {
                current.Append(c);
            }
        }

        // Flush remaining text
        string last = TextNormalizer.NormalizeAll(current.ToString());
        if(!string.IsNullOrWhiteSpace(last))
            blocks.Add(new ExtractedBlock(BlockType.Paragraph, last));

        return blocks;
    }

    private static byte[]? TryReadStream(RootStorage storage, string name)
    {
        try {
            using CfbStream stream = storage.OpenStream(name);
            using MemoryStream ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        } catch {
            return null;
        }
    }

    private static ExtractedDocument EmptyDocument(DocumentInput input) =>
        new() {
            Title = input.FileName != null ? Path.GetFileNameWithoutExtension(input.FileName) : "Untitled",
            Blocks = [],
            Metadata = new ExtractedDocumentMetadata(Format: DocFormat.Doc)
        };
}
