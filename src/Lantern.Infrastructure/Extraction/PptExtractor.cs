using System.Text;
using Lantern.Core.Extraction;
using OpenMcdf;

namespace Lantern.Infrastructure.Extraction;

/// <summary>
/// Extracts slide text from legacy PowerPoint 97–2003 (.ppt) documents using OpenMcdf.
/// Parses PowerPoint binary records to extract TextCharsAtom (UTF-16LE) and
/// TextBytesAtom (Latin-1) records. Speaker notes are also captured where present.
/// </summary>
public sealed class PptExtractor : IDocumentExtractor
{
    private const ushort RecTextCharsAtom = 0x0FA0; // UTF-16LE text
    private const ushort RecTextBytesAtom = 0x0FA8; // 8-bit text (Latin-1)
    private const ushort RecSlideAtom = 0x03E8;     // marks a new slide
    private const ushort RecNotesAtom = 0x03F0;     // marks a notes slide

    public bool CanExtract(DocFormat format) => format == DocFormat.Ppt;

    public Task<ExtractedDocument> ExtractAsync(DocumentInput input, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Extract(input), cancellationToken);
    }

    private static ExtractedDocument Extract(DocumentInput input)
    {
        string? filePath = input.FilePath;
        if(filePath == null)
            throw new ArgumentException("PptExtractor requires FilePath");

        try {
            using RootStorage storage = RootStorage.OpenRead(filePath);
            byte[]? data = TryReadStream(storage, "PowerPoint Document");
            if(data == null || data.Length < 8)
                return EmptyDocument(input);

            var blocks = ParseSlideBlocks(data);
            string title = input.FileName != null ? Path.GetFileNameWithoutExtension(input.FileName) : "Presentation";
            int slideCount = blocks.Count(b => b.SlideNumber.HasValue && b.Type == BlockType.Paragraph);

            return new ExtractedDocument {
                Title = title,
                Blocks = blocks,
                Metadata = new ExtractedDocumentMetadata(
                    Title: title,
                    Format: DocFormat.Ppt,
                    SlideCount: slideCount)
            };
        } catch(Exception ex) when(ex is not OperationCanceledException) {
            throw new InvalidOperationException($"Failed to open .ppt file: {ex.Message}", ex);
        }
    }

    private static List<ExtractedBlock> ParseSlideBlocks(byte[] data)
    {
        List<ExtractedBlock> blocks = new List<ExtractedBlock>();
        int slideNumber = 0;
        bool inNotes = false;
        int i = 0;

        while(i + 8 <= data.Length) {
            // Header: recVer/recInstance (2 bytes) + recType (2 bytes) + recLen (4 bytes)
            ushort typeHi = (ushort)((data[i + 3] << 8) | data[i + 2]);
            uint recLen = (uint)((data[i + 7] << 24) | (data[i + 6] << 16) | (data[i + 5] << 8) | data[i + 4]);

            if(typeHi == RecSlideAtom) {
                slideNumber++;
                inNotes = false;
            } else if(typeHi == RecNotesAtom) {
                inNotes = true;
            } else if(typeHi == RecTextCharsAtom && recLen > 0 && slideNumber > 0) {
                int dataStart = i + 8;
                int dataEnd = (int)Math.Min(dataStart + recLen, (uint)data.Length);
                if(dataEnd > dataStart) {
                    string text = Encoding.Unicode.GetString(data, dataStart, dataEnd - dataStart).TrimEnd('\0');
                    text = TextNormalizer.NormalizeAll(text);
                    if(!string.IsNullOrWhiteSpace(text)) {
                        blocks.Add(new ExtractedBlock(
                            Type: inNotes ? BlockType.Paragraph : BlockType.Paragraph,
                            Text: text,
                            SlideNumber: slideNumber,
                            SectionTitle: inNotes ? $"Slide {slideNumber} Notes" : null));
                    }
                }
            } else if(typeHi == RecTextBytesAtom && recLen > 0 && slideNumber > 0) {
                int dataStart = i + 8;
                int dataEnd = (int)Math.Min(dataStart + recLen, (uint)data.Length);
                if(dataEnd > dataStart) {
                    string text = Encoding.Latin1.GetString(data, dataStart, dataEnd - dataStart).TrimEnd('\0');
                    text = TextNormalizer.NormalizeAll(text);
                    if(!string.IsNullOrWhiteSpace(text)) {
                        blocks.Add(new ExtractedBlock(
                            Type: BlockType.Paragraph,
                            Text: text,
                            SlideNumber: slideNumber,
                            SectionTitle: inNotes ? $"Slide {slideNumber} Notes" : null));
                    }
                }
            }

            // Advance past this record
            long advance = 8L + recLen;
            if(advance <= 0 || advance > int.MaxValue) break;
            i += (int)advance;
        }

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
            Title = input.FileName != null ? Path.GetFileNameWithoutExtension(input.FileName) : "Presentation",
            Blocks = [],
            Metadata = new ExtractedDocumentMetadata(Format: DocFormat.Ppt)
        };
}
