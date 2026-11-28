using System.Buffers.Binary;
using System.Text;
using Lantern.Core.Extraction;
using OpenMcdf;

namespace Lantern.Infrastructure.Extraction;

/// <summary>
/// Extracts text from legacy Word 97–2003 (.doc) documents.
///
/// Uses OpenMcdf to open the CFBF container, then parses the Word Binary File
/// Format (MS-DOC) directly: FIB → piece table (Clx/Pcdt) → text pieces. Each
/// piece is either UTF-16LE or compressed 1-byte (CP1252 with a few remapped
/// high codepoints) depending on the fCompressed bit in its FC field. Japanese
/// and other non-Latin content is preserved through the UTF-16 branch.
///
/// Main document body, footnotes, headers/footers, annotations, endnotes and
/// text boxes are all walked. Macros and embedded OLE objects are ignored.
/// </summary>
public sealed class DocExtractor : IDocumentExtractor
{
    private const int MaxExtractedChars = 8_000_000;

    public bool CanExtract(DocFormat format) => format == DocFormat.Doc;

    public Task<ExtractedDocument> ExtractAsync(DocumentInput input, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Extract(input, cancellationToken), cancellationToken);
    }

    private static ExtractedDocument Extract(DocumentInput input, CancellationToken cancellationToken)
    {
        if(input.FilePath == null && input.Stream == null)
            throw new ArgumentException("DocExtractor requires FilePath or Stream");

        try {
            using RootStorage storage = input.FilePath != null
                ? RootStorage.OpenRead(input.FilePath)
                : RootStorage.Open(input.Stream!, StorageModeFlags.LeaveOpen);

            byte[]? wordDoc = TryReadStream(storage, "WordDocument");
            if(wordDoc == null || wordDoc.Length < 68)
                return EmptyDocument(input);

            FibHeader fib = ReadFib(wordDoc);
            if(fib.FEncrypted)
                throw new InvalidOperationException("DOC_ENCRYPTED: Document is encrypted or password-protected");

            string tableStreamName = fib.FWhichTblStm ? "1Table" : "0Table";
            byte[]? tableStream = TryReadStream(storage, tableStreamName)
                ?? TryReadStream(storage, fib.FWhichTblStm ? "0Table" : "1Table");

            List<ExtractedBlock> blocks;
            if(fib.FComplex && tableStream != null) {
                blocks = ExtractWithPieceTable(wordDoc, tableStream, fib, cancellationToken);
            } else {
                // Non-complex: text is stored contiguously in WordDocument from fibBase.fcMin.
                // For Word 97+ the FIB forces fComplex, so this branch mostly serves
                // damaged files that lack a readable Table stream.
                blocks = ExtractContiguous(wordDoc, fib, cancellationToken);
            }

            string title = ResolveTitle(input);
            return new ExtractedDocument {
                Title = title,
                Blocks = blocks,
                Metadata = new ExtractedDocumentMetadata(Title: title, Format: DocFormat.Doc)
            };
        } catch(InvalidOperationException) {
            throw;
        } catch(OperationCanceledException) {
            throw;
        } catch(Exception ex) {
            throw new InvalidOperationException("Failed to parse .doc file: " + ex.Message, ex);
        }
    }

    // ── FIB (File Information Block) parsing ─────────────────────────────────

    private readonly struct FibHeader
    {
        public ushort NFib { get; init; }
        public bool FComplex { get; init; }
        public bool FEncrypted { get; init; }
        public bool FWhichTblStm { get; init; }
        public uint FcMin { get; init; }
        public int CcpText { get; init; }
        public int CcpFtn { get; init; }
        public int CcpHdd { get; init; }
        public int CcpAtn { get; init; }
        public int CcpEdn { get; init; }
        public int CcpTxbx { get; init; }
        public int CcpHdrTxbx { get; init; }
        public uint FcClx { get; init; }
        public int LcbClx { get; init; }
    }

    private static FibHeader ReadFib(byte[] wd)
    {
        // fibBase: 32 bytes
        ushort wIdent = BinaryPrimitives.ReadUInt16LittleEndian(wd.AsSpan(0, 2));
        if(wIdent != 0xA5EC && wIdent != 0xA5DC && wIdent != 0xA699 && wIdent != 0xA697)
            throw new InvalidOperationException("Not a valid Word binary document (bad wIdent)");

        ushort nFib = BinaryPrimitives.ReadUInt16LittleEndian(wd.AsSpan(2, 2));
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(wd.AsSpan(0x0A, 2));
        bool fComplex = (flags & 0x0004) != 0;
        bool fEncrypted = (flags & 0x0100) != 0;
        bool fWhichTblStm = (flags & 0x0200) != 0;
        uint fcMin = BinaryPrimitives.ReadUInt32LittleEndian(wd.AsSpan(0x0018, 4));

        // After fibBase (32 bytes):
        //   csw (2) at 0x20
        //   fibRgW97 (csw*2)
        //   cslw (2)
        //   fibRgLw97 (cslw*4)   — contains ccpText etc.
        //   cbRgFcLcb (2)
        //   fibRgFcLcbBlob (cbRgFcLcb*8) — contains fcClx/lcbClx at index 33
        int off = 0x20;
        ushort csw = BinaryPrimitives.ReadUInt16LittleEndian(wd.AsSpan(off, 2));
        off += 2 + csw * 2;

        if(off + 2 > wd.Length)
            throw new InvalidOperationException("Truncated FIB (cslw)");
        ushort cslw = BinaryPrimitives.ReadUInt16LittleEndian(wd.AsSpan(off, 2));
        int fibRgLw97Off = off + 2;
        off = fibRgLw97Off + cslw * 4;

        if(fibRgLw97Off + 28 > wd.Length)
            throw new InvalidOperationException("Truncated fibRgLw97");

        // fibRgLw97 layout (first few longs after cbMac/reserved):
        //   cbMac         @ 0
        //   reserved1-2   @ 4..11
        //   ccpText       @ 12
        //   ccpFtn        @ 16
        //   ccpHdd        @ 20
        //   reserved      @ 24
        //   ccpAtn        @ 28
        //   ccpEdn        @ 32
        //   ccpTxbx       @ 36
        //   ccpHdrTxbx    @ 40
        int ccpText = BinaryPrimitives.ReadInt32LittleEndian(wd.AsSpan(fibRgLw97Off + 12, 4));
        int ccpFtn = BinaryPrimitives.ReadInt32LittleEndian(wd.AsSpan(fibRgLw97Off + 16, 4));
        int ccpHdd = BinaryPrimitives.ReadInt32LittleEndian(wd.AsSpan(fibRgLw97Off + 20, 4));
        int ccpAtn = BinaryPrimitives.ReadInt32LittleEndian(wd.AsSpan(fibRgLw97Off + 28, 4));
        int ccpEdn = BinaryPrimitives.ReadInt32LittleEndian(wd.AsSpan(fibRgLw97Off + 32, 4));
        int ccpTxbx = BinaryPrimitives.ReadInt32LittleEndian(wd.AsSpan(fibRgLw97Off + 36, 4));
        int ccpHdrTxbx = BinaryPrimitives.ReadInt32LittleEndian(wd.AsSpan(fibRgLw97Off + 40, 4));

        if(off + 2 > wd.Length)
            throw new InvalidOperationException("Truncated FIB (cbRgFcLcb)");
        ushort cbRgFcLcb = BinaryPrimitives.ReadUInt16LittleEndian(wd.AsSpan(off, 2));
        int blobOff = off + 2;

        // fcClx/lcbClx are entry 33 in fibRgFcLcbBlob (each entry is 8 bytes:
        // 4-byte FC + 4-byte LCB).
        const int clxIndex = 33;
        int clxEntryOff = blobOff + clxIndex * 8;
        if(clxIndex >= cbRgFcLcb || clxEntryOff + 8 > wd.Length)
            throw new InvalidOperationException("FIB blob too small for fcClx");

        uint fcClx = BinaryPrimitives.ReadUInt32LittleEndian(wd.AsSpan(clxEntryOff, 4));
        int lcbClx = BinaryPrimitives.ReadInt32LittleEndian(wd.AsSpan(clxEntryOff + 4, 4));

        return new FibHeader {
            NFib = nFib,
            FComplex = fComplex,
            FEncrypted = fEncrypted,
            FWhichTblStm = fWhichTblStm,
            FcMin = fcMin,
            CcpText = ccpText,
            CcpFtn = ccpFtn,
            CcpHdd = ccpHdd,
            CcpAtn = ccpAtn,
            CcpEdn = ccpEdn,
            CcpTxbx = ccpTxbx,
            CcpHdrTxbx = ccpHdrTxbx,
            FcClx = fcClx,
            LcbClx = lcbClx
        };
    }

    // ── Piece table (Clx/Pcdt) walker ────────────────────────────────────────

    /// <summary>
    /// A single Pcd piece: a CP range and the WordDocument byte offset where
    /// the characters live, plus whether they are stored as 1-byte compressed
    /// or 2-byte UTF-16LE.
    /// </summary>
    private readonly struct Piece
    {
        public int CpStart { get; init; }
        public int CpEnd { get; init; }
        public uint Fc { get; init; }
        public bool Compressed { get; init; }
    }

    private static List<Piece> ReadPieceTable(byte[] table, FibHeader fib)
    {
        if(fib.FcClx + fib.LcbClx > table.Length)
            throw new InvalidOperationException("Clx range exceeds table stream");

        // Walk CLX: optional Prc blocks (starting byte 0x01), then one Pcdt (0x02).
        int p = (int)fib.FcClx;
        int end = p + fib.LcbClx;
        while(p < end) {
            byte clxt = table[p];
            if(clxt == 0x01) {
                // Prc: 0x01 + 2-byte len + <len> bytes
                if(p + 3 > end) break;
                short cbGrpprl = BinaryPrimitives.ReadInt16LittleEndian(table.AsSpan(p + 1, 2));
                p += 3 + Math.Max(0, (int)cbGrpprl);
            } else if(clxt == 0x02) {
                // Pcdt: 0x02 + 4-byte lcb + PlcPcd
                if(p + 5 > end) break;
                int lcb = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(p + 1, 4));
                int plcStart = p + 5;
                return ReadPlcPcd(table, plcStart, lcb);
            } else {
                // Unknown marker — bail.
                break;
            }
        }
        throw new InvalidOperationException("Clx does not contain a Pcdt block");
    }

    private static List<Piece> ReadPlcPcd(byte[] table, int offset, int length)
    {
        // PlcPcd layout: (n+1) CP entries of 4 bytes each + n Pcd entries of 8 bytes each.
        //   8n + 4(n+1) = length   →   n = (length - 4) / 12
        if(length < 4 || offset + length > table.Length)
            throw new InvalidOperationException("Invalid PlcPcd length");
        int n = (length - 4) / 12;
        if(n <= 0)
            return new List<Piece>();

        int cpArrayOff = offset;
        int pcdArrayOff = offset + 4 * (n + 1);

        List<Piece> pieces = new List<Piece>(n);
        for(int i = 0; i < n; i++) {
            int cpStart = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(cpArrayOff + i * 4, 4));
            int cpEnd = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(cpArrayOff + (i + 1) * 4, 4));

            int pcdOff = pcdArrayOff + i * 8;
            // Pcd: 2-byte flag, 4-byte fc_compressed, 2-byte prm
            uint rawFc = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(pcdOff + 2, 4));
            bool compressed = (rawFc & 0x40000000) != 0;
            uint fc = rawFc & 0x3FFFFFFF;
            if(compressed) fc /= 2;

            pieces.Add(new Piece {
                CpStart = cpStart,
                CpEnd = cpEnd,
                Fc = fc,
                Compressed = compressed
            });
        }

        return pieces;
    }

    // ── Text extraction ──────────────────────────────────────────────────────

    private static List<ExtractedBlock> ExtractWithPieceTable(byte[] wd, byte[] table, FibHeader fib, CancellationToken ct)
    {
        List<Piece> pieces = ReadPieceTable(table, fib);
        if(pieces.Count == 0) return new List<ExtractedBlock>();

        StringBuilder full = new StringBuilder();
        int totalChars = 0;
        foreach(Piece piece in pieces) {
            ct.ThrowIfCancellationRequested();
            if(piece.CpEnd <= piece.CpStart) continue;

            int count = piece.CpEnd - piece.CpStart;
            if(piece.Compressed) {
                if(piece.Fc + count > wd.Length) break;
                for(int k = 0; k < count && totalChars < MaxExtractedChars; k++) {
                    byte b = wd[piece.Fc + k];
                    full.Append(DecodeCompressed(b));
                    totalChars++;
                }
            } else {
                int byteCount = count * 2;
                if(piece.Fc + byteCount > wd.Length) break;
                int readLen = Math.Min(byteCount, (MaxExtractedChars - totalChars) * 2);
                if(readLen <= 0) break;
                string chunk = Encoding.Unicode.GetString(wd, (int)piece.Fc, readLen);
                full.Append(chunk);
                totalChars += chunk.Length;
            }

            if(totalChars >= MaxExtractedChars) break;
        }

        // Partition the character stream into: main doc | footnotes | headers | annotations | endnotes | textboxes | hdr-textboxes
        string text = full.ToString();
        List<ExtractedBlock> blocks = new List<ExtractedBlock>();
        int cursor = 0;

        AppendSection(blocks, text, ref cursor, fib.CcpText, null, BlockType.Paragraph);
        AppendSection(blocks, text, ref cursor, fib.CcpFtn, "Footnotes", BlockType.Paragraph);
        AppendSection(blocks, text, ref cursor, fib.CcpHdd, "Headers and Footers", BlockType.Paragraph);
        AppendSection(blocks, text, ref cursor, fib.CcpAtn, "Comments", BlockType.Paragraph);
        AppendSection(blocks, text, ref cursor, fib.CcpEdn, "Endnotes", BlockType.Paragraph);
        AppendSection(blocks, text, ref cursor, fib.CcpTxbx, "Text Boxes", BlockType.Paragraph);
        AppendSection(blocks, text, ref cursor, fib.CcpHdrTxbx, "Header Text Boxes", BlockType.Paragraph);

        return blocks;
    }

    private static List<ExtractedBlock> ExtractContiguous(byte[] wd, FibHeader fib, CancellationToken ct)
    {
        // Simple fall-back: assume the main document body is the UTF-16 range
        // [fcMin, fcMin + ccpText*2). This matches very old non-complex docs.
        int fc = (int)fib.FcMin;
        int count = Math.Max(0, Math.Min(fib.CcpText, (wd.Length - fc) / 2));
        if(fc >= wd.Length || count <= 0)
            return new List<ExtractedBlock>();
        string text = Encoding.Unicode.GetString(wd, fc, count * 2);
        List<ExtractedBlock> blocks = new List<ExtractedBlock>();
        int cursor = 0;
        AppendSection(blocks, text, ref cursor, count, null, BlockType.Paragraph);
        return blocks;
    }

    private static void AppendSection(List<ExtractedBlock> blocks, string text, ref int cursor, int ccp, string? sectionTitle, BlockType type)
    {
        if(ccp <= 0 || cursor >= text.Length) return;
        int end = Math.Min(cursor + ccp, text.Length);
        string segment = text.Substring(cursor, end - cursor);
        cursor = end;

        foreach(string paragraph in SplitParagraphs(segment)) {
            string normalized = TextNormalizer.NormalizeAll(paragraph);
            if(string.IsNullOrWhiteSpace(normalized)) continue;
            blocks.Add(new ExtractedBlock(
                Type: type,
                Text: normalized,
                SectionTitle: sectionTitle));
        }
    }

    /// <summary>
    /// Word's piece text uses these control codes:
    ///   0x0D (CR)   paragraph mark
    ///   0x0B (VT)   soft line break
    ///   0x07 (BEL)  table cell mark
    ///   0x0C (FF)   section/page break
    ///   0x13/15     field begin/end markers
    ///   0x14        field separator — everything from 0x13..0x14 is field code, keep 0x14..0x15 as result
    ///   0x08        picture/object placeholder
    /// We treat CR as the paragraph splitter, map other breaks to spaces and
    /// strip field codes.
    /// </summary>
    private static IEnumerable<string> SplitParagraphs(string segment)
    {
        StringBuilder current = new StringBuilder();
        bool inFieldCode = false;
        for(int i = 0; i < segment.Length; i++) {
            char c = segment[i];
            if(c == '\u0013') { inFieldCode = true; continue; }
            if(c == '\u0014') { inFieldCode = false; continue; }
            if(c == '\u0015') { inFieldCode = false; continue; }
            if(inFieldCode) continue;

            if(c == '\r' || c == '\f') {
                yield return current.ToString();
                current.Clear();
                continue;
            }
            if(c == '\u0007' || c == '\u000B') {
                current.Append(' ');
                continue;
            }
            if(c == '\u0008' || c == '\u0001' || c == '\u0002') {
                // Picture/footnote markers — ignore the single-char placeholder.
                continue;
            }
            if(c == '\u0000') continue;
            current.Append(c);
        }
        if(current.Length > 0) yield return current.ToString();
    }

    /// <summary>
    /// Decode a single compressed byte. For Word, bytes 0x80..0xFF map to a
    /// handful of CP1252 high codepoints; everything else is its own ASCII
    /// value. See [MS-DOC] §2.4.1 "Retrieving Text".
    /// </summary>
    private static char DecodeCompressed(byte b) => b switch {
        0x82 => '\u201A',
        0x83 => '\u0192',
        0x84 => '\u201E',
        0x85 => '\u2026',
        0x86 => '\u2020',
        0x87 => '\u2021',
        0x88 => '\u02C6',
        0x89 => '\u2030',
        0x8A => '\u0160',
        0x8B => '\u2039',
        0x8C => '\u0152',
        0x91 => '\u2018',
        0x92 => '\u2019',
        0x93 => '\u201C',
        0x94 => '\u201D',
        0x95 => '\u2022',
        0x96 => '\u2013',
        0x97 => '\u2014',
        0x98 => '\u02DC',
        0x99 => '\u2122',
        0x9A => '\u0161',
        0x9B => '\u203A',
        0x9C => '\u0153',
        0x9F => '\u0178',
        _ => (char)b
    };

    // ── Helpers ──────────────────────────────────────────────────────────────

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
        return name != null ? Path.GetFileNameWithoutExtension(name) : "Untitled";
    }

    private static ExtractedDocument EmptyDocument(DocumentInput input) =>
        new() {
            Title = ResolveTitle(input),
            Blocks = [],
            Metadata = new ExtractedDocumentMetadata(Format: DocFormat.Doc)
        };
}
