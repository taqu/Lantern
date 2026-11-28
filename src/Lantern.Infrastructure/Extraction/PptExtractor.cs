using System.Buffers.Binary;
using System.Text;
using Lantern.Core.Extraction;
using OpenMcdf;

namespace Lantern.Infrastructure.Extraction;

/// <summary>
/// Extracts slide text from legacy PowerPoint 97–2003 (.ppt) documents.
///
/// The .ppt binary format stores a tree of records inside the
/// <c>PowerPoint Document</c> compound stream. Each record has an 8-byte
/// header: a 2-byte recVer/recInstance, a 2-byte recType, and a 4-byte
/// recLen. Container records are marked by <c>recVer == 0x0F</c> and nest
/// child records in their body; atom records carry raw payload.
///
/// This extractor walks the record tree recursively, descending into every
/// container. Slide text is harvested from <c>TextCharsAtom</c> (0x0FA0,
/// UTF-16LE) and <c>TextBytesAtom</c> (0x0FA8, CP1252). A <c>SlideContainer</c>
/// (0x03EE) opens a new slide; a <c>NotesContainer</c> (0x03EF) opens the
/// speaker-notes scope for the current slide; a <c>SlideListWithText</c>
/// container (0x0FF0) contains cached text for all slides at the top level.
/// </summary>
public sealed class PptExtractor : IDocumentExtractor
{
    private const ushort RtSlide = 0x03EE;              // SlideContainer
    private const ushort RtNotes = 0x03EF;              // NotesContainer
    private const ushort RtSlideListWithText = 0x0FF0;  // top-level cached text per slide
    private const ushort RtStSlide = 0x03F8;            // SlideListWithText child atoms
    private const ushort RtTextCharsAtom = 0x0FA0;      // UTF-16LE text run
    private const ushort RtTextBytesAtom = 0x0FA8;      // CP1252 1-byte text run
    private const ushort RtCString = 0x0FBA;            // UTF-16LE string (used for title, etc.)
    private const ushort RtDocument = 0x03E8;           // top-level document container
    private const ushort RtMainMaster = 0x03F0;         // master slide container (ignore its text)

    private const int MaxExtractedChars = 8_000_000;
    private const int MaxRecordDepth = 64;

    public bool CanExtract(DocFormat format) => format == DocFormat.Ppt;

    public Task<ExtractedDocument> ExtractAsync(DocumentInput input, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Extract(input, cancellationToken), cancellationToken);
    }

    private static ExtractedDocument Extract(DocumentInput input, CancellationToken cancellationToken)
    {
        if(input.FilePath == null && input.Stream == null)
            throw new ArgumentException("PptExtractor requires FilePath or Stream");

        try {
            using RootStorage storage = input.FilePath != null
                ? RootStorage.OpenRead(input.FilePath)
                : RootStorage.Open(input.Stream!, StorageModeFlags.LeaveOpen);

            byte[]? data = TryReadStream(storage, "PowerPoint Document");
            if(data == null || data.Length < 8)
                return EmptyDocument(input);

            // Encrypted presentations use the "EncryptedSummary" and "EncryptedPackage"
            // streams rather than "PowerPoint Document" — if we only see those, the file
            // is password-protected.
            if(IsLikelyEncrypted(storage, data))
                throw new InvalidOperationException("DOC_ENCRYPTED: Presentation is password-protected");

            WalkState state = new WalkState();
            WalkRecords(data, 0, data.Length, state, depth: 0, cancellationToken);

            List<ExtractedBlock> blocks = state.Blocks;
            string title = ResolveTitle(input);
            int slideCount = CountSlides(blocks);

            return new ExtractedDocument {
                Title = title,
                Blocks = blocks,
                Metadata = new ExtractedDocumentMetadata(
                    Title: title,
                    Format: DocFormat.Ppt,
                    SlideCount: slideCount)
            };
        } catch(InvalidOperationException) {
            throw;
        } catch(OperationCanceledException) {
            throw;
        } catch(Exception ex) {
            throw new InvalidOperationException("Failed to parse .ppt file: " + ex.Message, ex);
        }
    }

    // ── Record tree walker ───────────────────────────────────────────────────

    private sealed class WalkState
    {
        public int SlideNumber { get; set; } = 0;
        public bool InNotes { get; set; } = false;
        public bool InMaster { get; set; } = false;
        public bool InSlideListWithText { get; set; } = false;
        public int CurrentSlideListIndex { get; set; } = 0;
        public int TotalChars { get; set; } = 0;
        public List<ExtractedBlock> Blocks { get; } = new();
    }

    private static void WalkRecords(byte[] data, int start, int end, WalkState state, int depth, CancellationToken ct)
    {
        if(depth > MaxRecordDepth) return;
        if(state.TotalChars >= MaxExtractedChars) return;

        int i = start;
        while(i + 8 <= end) {
            ct.ThrowIfCancellationRequested();

            ushort verInst = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i, 2));
            ushort recType = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i + 2, 2));
            uint recLen = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i + 4, 4));
            int bodyStart = i + 8;
            long bodyEndLong = (long)bodyStart + recLen;
            if(bodyEndLong > end || bodyEndLong < bodyStart) break;
            int bodyEnd = (int)bodyEndLong;

            bool isContainer = (verInst & 0x000F) == 0x000F;

            if(isContainer) {
                // Track logical context before descending.
                bool prevInNotes = state.InNotes;
                bool prevInMaster = state.InMaster;
                bool prevInSlwt = state.InSlideListWithText;

                switch(recType) {
                    case RtSlide:
                        state.SlideNumber++;
                        state.InNotes = false;
                        state.InMaster = false;
                        break;
                    case RtNotes:
                        state.InNotes = true;
                        break;
                    case RtMainMaster:
                        state.InMaster = true;
                        break;
                    case RtSlideListWithText:
                        state.InSlideListWithText = true;
                        state.CurrentSlideListIndex = 0;
                        break;
                }

                WalkRecords(data, bodyStart, bodyEnd, state, depth + 1, ct);

                state.InNotes = prevInNotes;
                state.InMaster = prevInMaster;
                state.InSlideListWithText = prevInSlwt;
            } else {
                HandleAtom(data, recType, verInst, bodyStart, bodyEnd, state);
            }

            i = bodyEnd;
        }
    }

    private static void HandleAtom(byte[] data, ushort recType, ushort verInst, int bodyStart, int bodyEnd, WalkState state)
    {
        if(state.TotalChars >= MaxExtractedChars) return;
        if(bodyEnd <= bodyStart) return;
        if(state.InMaster) return; // skip master-slide templates

        switch(recType) {
            case RtTextCharsAtom: {
                string text = Encoding.Unicode.GetString(data, bodyStart, bodyEnd - bodyStart);
                EmitText(state, text);
                break;
            }
            case RtTextBytesAtom: {
                string text = Encoding.Latin1.GetString(data, bodyStart, bodyEnd - bodyStart);
                EmitText(state, text);
                break;
            }
            case RtCString when state.InSlideListWithText: {
                // CString inside SlideListWithText holds cached text per placeholder
                string text = Encoding.Unicode.GetString(data, bodyStart, bodyEnd - bodyStart);
                EmitText(state, text);
                break;
            }
            case RtStSlide when state.InSlideListWithText: {
                // When we encounter a per-slide atom inside SlideListWithText, advance
                // the virtual slide index so cached runs map to the right slide.
                state.CurrentSlideListIndex++;
                if(state.SlideNumber == 0)
                    state.SlideNumber = state.CurrentSlideListIndex;
                break;
            }
        }
    }

    private static void EmitText(WalkState state, string raw)
    {
        if(string.IsNullOrEmpty(raw)) return;

        // Each atom may contain multiple runs separated by paragraph marks
        // (0x0D CR or 0x0B VT, soft line-break). Split so chunk boundaries
        // line up with visible paragraph boundaries.
        int remaining = MaxExtractedChars - state.TotalChars;
        if(remaining <= 0) return;
        if(raw.Length > remaining) raw = raw.Substring(0, remaining);

        int slide = state.SlideNumber > 0 ? state.SlideNumber : 1;
        string? sectionTitle = state.InNotes ? $"Slide {slide} Notes" : null;

        foreach(string paragraph in SplitParagraphs(raw)) {
            string normalized = TextNormalizer.NormalizeAll(paragraph);
            if(string.IsNullOrWhiteSpace(normalized)) continue;
            state.Blocks.Add(new ExtractedBlock(
                Type: BlockType.Paragraph,
                Text: normalized,
                SectionTitle: sectionTitle,
                SlideNumber: slide));
            state.TotalChars += normalized.Length;
        }
    }

    private static IEnumerable<string> SplitParagraphs(string text)
    {
        StringBuilder current = new StringBuilder();
        for(int i = 0; i < text.Length; i++) {
            char c = text[i];
            if(c == '\r' || c == '\n') {
                yield return current.ToString();
                current.Clear();
                if(c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                continue;
            }
            if(c == '\u000B') {
                // Soft line-break — keep in the same paragraph as a space so
                // callers see the original word order.
                current.Append(' ');
                continue;
            }
            if(c == '\u0000') continue;
            current.Append(c);
        }
        if(current.Length > 0) yield return current.ToString();
    }

    private static int CountSlides(List<ExtractedBlock> blocks)
    {
        int max = 0;
        foreach(ExtractedBlock b in blocks)
            if(b.SlideNumber.HasValue && b.SlideNumber.Value > max)
                max = b.SlideNumber.Value;
        return max;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsLikelyEncrypted(RootStorage storage, byte[] powerPointDoc)
    {
        if(TryReadStream(storage, "EncryptedPackage") != null) return true;
        if(TryReadStream(storage, "EncryptionInfo") != null) return true;
        // In PPT97 encryption, the stream contents start with a Document
        // container (recType 0x03E8); if we cannot find any slide/document
        // records *and* the data looks like random bytes, assume encryption.
        return false;
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

    private static string ResolveTitle(DocumentInput input)
    {
        string? name = input.FileName
            ?? (input.FilePath != null ? Path.GetFileName(input.FilePath) : null);
        return name != null ? Path.GetFileNameWithoutExtension(name) : "Presentation";
    }

    private static ExtractedDocument EmptyDocument(DocumentInput input) =>
        new() {
            Title = ResolveTitle(input),
            Blocks = [],
            Metadata = new ExtractedDocumentMetadata(Format: DocFormat.Ppt)
        };
}
