using System.Buffers.Binary;
using System.Text;
using Lantern.Core.Extraction;
using Lantern.Infrastructure.Extraction;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using OpenMcdf;

namespace Lantern.IntegrationTests.Extraction;

/// <summary>
/// Tests for legacy Office format extractors (.doc, .xls, .ppt).
///
/// For <c>.xls</c>, fixtures are produced through NPOI's HSSFWorkbook so we are
/// exercising the exact code path real workbooks use. For <c>.doc</c> and
/// <c>.ppt</c> we assemble minimal but spec-faithful binary streams
/// (FIB + Clx piece table for Word; container record tree for PowerPoint)
/// into OpenMcdf-created CFBF containers — the resulting bytes match the
/// structure a saved Office 2003 document would produce.
/// </summary>
public sealed class LegacyOfficeExtractionTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"lantern-legacy-{Guid.NewGuid():N}");

    public LegacyOfficeExtractionTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ── DOC fixture ──────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal but valid Word 97–2003 binary file:
    ///   WordDocument: FIB (fComplex=1, fWhichTblStm=1) + UTF-16 text payload
    ///   1Table:       Clx containing a Pcdt with one PCD per paragraph
    /// </summary>
    private string CreateDocFile(params string[] paragraphs)
    {
        string path = Path.Combine(_tempDir, $"test-{Guid.NewGuid():N}.doc");
        CreateDocFile(path, paragraphs);
        return path;
    }

    private static void CreateDocFile(string path, string[] paragraphs)
    {
        // Each paragraph is terminated by CR (0x000D) in Word's text stream.
        // We encode the whole concatenated text as UTF-16LE and point one PCD
        // at the full range (uncompressed).
        StringBuilder textBuilder = new StringBuilder();
        foreach(string paragraph in paragraphs) {
            textBuilder.Append(paragraph);
            textBuilder.Append('\r');
        }
        string text = textBuilder.ToString();
        byte[] textBytes = Encoding.Unicode.GetBytes(text);
        int ccpText = text.Length;

        // FIB layout we emit (matches Word 97 shape, fWhichTblStm=1 → use "1Table"):
        //   fibBase     32 bytes (wIdent=0xA5EC)
        //   csw         2 bytes  = 0x000E
        //   fibRgW97    28 bytes = zeros
        //   cslw        2 bytes  = 0x0016
        //   fibRgLw97   88 bytes (ccpText at offset 12)
        //   cbRgFcLcb   2 bytes  = 0x005D (93 entries)
        //   fibRgFcLcbBlob 744 bytes (fcClx/lcbClx at entry 33)
        const int fibLen = 32 + 2 + 28 + 2 + 88 + 2 + 744;
        byte[] wordDoc = new byte[fibLen + textBytes.Length];

        // fibBase.wIdent
        BinaryPrimitives.WriteUInt16LittleEndian(wordDoc.AsSpan(0, 2), 0xA5EC);
        // fibBase.nFib = 0x00C1 (Word 97)
        BinaryPrimitives.WriteUInt16LittleEndian(wordDoc.AsSpan(2, 2), 0x00C1);
        // fibBase.flags at 0x0A: set fComplex (0x0004) + fWhichTblStm (0x0200)
        BinaryPrimitives.WriteUInt16LittleEndian(wordDoc.AsSpan(0x0A, 2), 0x0004 | 0x0200);

        // csw
        BinaryPrimitives.WriteUInt16LittleEndian(wordDoc.AsSpan(0x20, 2), 0x000E);
        // cslw lives at 0x20 + 2 + 28 = 0x3E
        BinaryPrimitives.WriteUInt16LittleEndian(wordDoc.AsSpan(0x3E, 2), 0x0016);

        // fibRgLw97 starts at 0x40 — ccpText at +12
        BinaryPrimitives.WriteInt32LittleEndian(wordDoc.AsSpan(0x40 + 12, 4), ccpText);

        // cbRgFcLcb at 0x40 + 88 = 0x98
        BinaryPrimitives.WriteUInt16LittleEndian(wordDoc.AsSpan(0x98, 2), 0x005D);
        // Blob starts at 0x9A, fcClx at entry 33 → 0x9A + 264 = 0x1A2
        int blobOff = 0x9A;
        int clxEntryOff = blobOff + 33 * 8;

        // Text payload goes right after the FIB.
        int textFc = fibLen;
        Buffer.BlockCopy(textBytes, 0, wordDoc, textFc, textBytes.Length);

        // Build the 1Table stream: Clx = Pcdt (0x02) + lcb + PlcPcd
        //   PlcPcd: (n+1) CP entries + n PCDs; here n = 1 (the whole text)
        //   CP[0] = 0, CP[1] = ccpText
        //   PCD: 2-byte flag + 4-byte fc (bit 30 cleared → UTF-16 at offset textFc) + 2-byte prm
        int plcLen = 4 * 2 + 8 * 1;  // 2 CPs + 1 PCD
        byte[] clx = new byte[1 + 4 + plcLen];
        clx[0] = 0x02;
        BinaryPrimitives.WriteInt32LittleEndian(clx.AsSpan(1, 4), plcLen);
        BinaryPrimitives.WriteInt32LittleEndian(clx.AsSpan(5, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(clx.AsSpan(9, 4), ccpText);
        // PCD at offset 13: 2-byte flag = 0, 4-byte fc, 2-byte prm = 0
        BinaryPrimitives.WriteUInt32LittleEndian(clx.AsSpan(15, 4), (uint)textFc);

        // Point fcClx/lcbClx at the start of the Table stream.
        BinaryPrimitives.WriteUInt32LittleEndian(wordDoc.AsSpan(clxEntryOff, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(wordDoc.AsSpan(clxEntryOff + 4, 4), clx.Length);

        using RootStorage storage = RootStorage.Create(path);
        using(CfbStream wd = storage.CreateStream("WordDocument"))
            wd.Write(wordDoc, 0, wordDoc.Length);
        using(CfbStream tbl = storage.CreateStream("1Table"))
            tbl.Write(clx, 0, clx.Length);
    }

    // ── XLS fixture (unchanged — exercises the real HSSF code path) ──────────

    private string CreateXlsFile(params (string sheetName, string[] rows)[] sheets)
    {
        string path = Path.Combine(_tempDir, $"test-{Guid.NewGuid():N}.xls");
        HSSFWorkbook workbook = new HSSFWorkbook();

        foreach(var (sheetName, rows) in sheets) {
            ISheet sheet = workbook.CreateSheet(sheetName);
            for(int r = 0; r < rows.Length; r++) {
                IRow row = sheet.CreateRow(r);
                row.CreateCell(0).SetCellValue(rows[r]);
            }
        }

        using var fs = File.Create(path);
        workbook.Write(fs);
        return path;
    }

    // ── PPT fixture ──────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal .ppt file by wrapping text runs inside slide and notes
    /// container records so the extractor exercises its recursive descent.
    /// </summary>
    private string CreatePptFile(params (string text, bool isNotes)[] entries)
    {
        string path = Path.Combine(_tempDir, $"test-{Guid.NewGuid():N}.ppt");

        MemoryStream contents = new MemoryStream();
        foreach(var (text, isNotes) in entries) {
            byte[] textPayload = Encoding.Unicode.GetBytes(text);
            byte[] textCharsAtom = BuildAtom(0x0FA0, textPayload);
            byte[] body;
            if(isNotes) {
                // SlideContainer (0x03EE) with an embedded NotesContainer (0x03EF)
                // that owns the text. Real presentations place the notes
                // container adjacent to its slide; wrapping makes the fixture
                // self-contained for testing.
                byte[] notes = BuildContainer(0x03EF, textCharsAtom);
                body = notes;
            } else {
                body = textCharsAtom;
            }
            byte[] slide = BuildContainer(0x03EE, body);
            contents.Write(slide, 0, slide.Length);
        }

        using RootStorage storage = RootStorage.Create(path);
        using CfbStream stream = storage.CreateStream("PowerPoint Document");
        byte[] all = contents.ToArray();
        stream.Write(all, 0, all.Length);

        return path;
    }

    private static byte[] BuildContainer(ushort recType, byte[] body)
    {
        byte[] bytes = new byte[8 + body.Length];
        // recVer/recInstance: low 4 bits == 0x0F marks a container.
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0, 2), 0x000F);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2, 2), recType);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), (uint)body.Length);
        Buffer.BlockCopy(body, 0, bytes, 8, body.Length);
        return bytes;
    }

    private static byte[] BuildAtom(ushort recType, byte[] body)
    {
        byte[] bytes = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0, 2), 0x0000);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2, 2), recType);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), (uint)body.Length);
        Buffer.BlockCopy(body, 0, bytes, 8, body.Length);
        return bytes;
    }

    // ── DocExtractor tests ────────────────────────────────────────────────────

    [Fact]
    public async Task Doc_ExtractsEnglishParagraphs()
    {
        string path = CreateDocFile("Hello World", "Second paragraph", "Third entry");
        DocExtractor extractor = new DocExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        Assert.NotEmpty(result.Blocks);
        List<string> texts = result.Blocks.Select(b => b.Text).ToList();
        Assert.Contains("Hello World", texts);
        Assert.Contains("Second paragraph", texts);
        Assert.Contains("Third entry", texts);
        Assert.Equal(DocFormat.Doc, result.Metadata.Format);
    }

    [Fact]
    public async Task Doc_ExtractsJapaneseText()
    {
        string path = CreateDocFile("日本語テスト", "二番目の段落");
        DocExtractor extractor = new DocExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        Assert.NotEmpty(result.Blocks);
        List<string> texts = result.Blocks.Select(b => b.Text).ToList();
        Assert.Contains("日本語テスト", texts);
        Assert.Contains("二番目の段落", texts);
        Assert.Equal(DocFormat.Doc, result.Metadata.Format);
    }

    [Fact]
    public async Task Doc_ParagraphBoundariesArePreserved()
    {
        string path = CreateDocFile("First line", "Second line", "Third line");
        DocExtractor extractor = new DocExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        // Each paragraph should emit as its own block.
        Assert.Equal(3, result.Blocks.Count);
    }

    [Fact]
    public async Task Doc_TitleIsFilenameWithoutExtension()
    {
        string path = CreateDocFile("Content");
        DocExtractor extractor = new DocExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        Assert.Equal(Path.GetFileNameWithoutExtension(path), result.Title);
    }

    [Fact]
    public async Task Doc_EmptyWordDocumentStream_ReturnsEmpty()
    {
        string path = Path.Combine(_tempDir, "empty.doc");
        using(RootStorage storage = RootStorage.Create(path)) {
            using CfbStream s = storage.CreateStream("WordDocument");
            s.Write(new byte[16]); // less than the 68-byte minimum to parse a FIB
        }

        DocExtractor extractor = new DocExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        Assert.Empty(result.Blocks);
        Assert.Equal(DocFormat.Doc, result.Metadata.Format);
    }

    [Fact]
    public async Task Doc_MissingWordDocumentStream_ReturnsEmpty()
    {
        string path = Path.Combine(_tempDir, "no-stream.doc");
        using(RootStorage storage = RootStorage.Create(path)) {
            // No streams added — extractor should not throw
        }

        DocExtractor extractor = new DocExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        Assert.Empty(result.Blocks);
    }

    [Fact]
    public async Task Doc_Encrypted_ThrowsEncryptedMarker()
    {
        string path = Path.Combine(_tempDir, "encrypted.doc");
        // Minimum FIB with fEncrypted flag set.
        byte[] wordDoc = new byte[1024];
        BinaryPrimitives.WriteUInt16LittleEndian(wordDoc.AsSpan(0, 2), 0xA5EC);
        BinaryPrimitives.WriteUInt16LittleEndian(wordDoc.AsSpan(0x0A, 2), 0x0100);  // fEncrypted
        BinaryPrimitives.WriteUInt16LittleEndian(wordDoc.AsSpan(0x20, 2), 0x000E);
        BinaryPrimitives.WriteUInt16LittleEndian(wordDoc.AsSpan(0x3E, 2), 0x0016);
        BinaryPrimitives.WriteUInt16LittleEndian(wordDoc.AsSpan(0x98, 2), 0x005D);
        using(RootStorage storage = RootStorage.Create(path)) {
            using CfbStream s = storage.CreateStream("WordDocument");
            s.Write(wordDoc, 0, wordDoc.Length);
        }
        DocExtractor extractor = new DocExtractor();
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => extractor.ExtractAsync(DocumentInput.FromFile(path)));
        Assert.StartsWith("DOC_ENCRYPTED", ex.Message);
    }

    [Fact]
    public void Doc_CanExtract_MatchesOnlyDocFormat()
    {
        DocExtractor extractor = new DocExtractor();
        Assert.True(extractor.CanExtract(DocFormat.Doc));
        Assert.False(extractor.CanExtract(DocFormat.Docx));
        Assert.False(extractor.CanExtract(DocFormat.Ppt));
        Assert.False(extractor.CanExtract(DocFormat.Unknown));
    }

    // ── XlsExtractor tests ───────────────────────────────────────────────────

    [Fact]
    public async Task Xls_ExtractsEnglishRows()
    {
        string path = CreateXlsFile(("Sheet1", ["Alpha", "Beta", "Gamma"]));
        XlsExtractor extractor = new XlsExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        Assert.NotEmpty(result.Blocks);
        string allText = string.Join(" ", result.Blocks.Select(b => b.Text));
        Assert.Contains("Alpha", allText);
        Assert.Contains("Beta", allText);
        Assert.Contains("Gamma", allText);
        Assert.Equal(DocFormat.Xls, result.Metadata.Format);
    }

    [Fact]
    public async Task Xls_ExtractsJapaneseContent()
    {
        string path = CreateXlsFile(("シート1", ["日本語データ", "二行目"]));
        XlsExtractor extractor = new XlsExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        Assert.NotEmpty(result.Blocks);
        string allText = string.Join(" ", result.Blocks.Select(b => b.Text));
        Assert.Contains("日本語データ", allText);
        Assert.Equal(DocFormat.Xls, result.Metadata.Format);
    }

    [Fact]
    public async Task Xls_MultipleSheets_ProducesBlocksPerSheet()
    {
        string path = CreateXlsFile(
            ("Employees", ["Alice", "Bob"]),
            ("Products", ["Widget", "Gadget"]));
        XlsExtractor extractor = new XlsExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        Assert.NotEmpty(result.Blocks);
        List<string?> sheetNames = result.Blocks.Select(b => b.SheetName).Distinct().ToList();
        Assert.Contains("Employees", sheetNames);
        Assert.Contains("Products", sheetNames);
    }

    [Fact]
    public async Task Xls_WorksheetNamesPopulated()
    {
        string path = CreateXlsFile(
            ("Alpha", ["data"]),
            ("Beta", ["more data"]));
        XlsExtractor extractor = new XlsExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        Assert.NotNull(result.Metadata.WorksheetNames);
        Assert.Contains("Alpha", result.Metadata.WorksheetNames!);
        Assert.Contains("Beta", result.Metadata.WorksheetNames!);
    }

    [Fact]
    public async Task Xls_NumericAndTextCells_AreExtracted()
    {
        string path = Path.Combine(_tempDir, $"numeric-{Guid.NewGuid():N}.xls");
        HSSFWorkbook workbook = new HSSFWorkbook();
        ISheet sheet = workbook.CreateSheet("Data");
        IRow row = sheet.CreateRow(0);
        row.CreateCell(0).SetCellValue("Label");
        row.CreateCell(1).SetCellValue(42.0);
        row.CreateCell(2).SetCellValue(true);
        using(FileStream fs = File.Create(path)) workbook.Write(fs);

        XlsExtractor extractor = new XlsExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        string flat = string.Join(" ", result.Blocks.Select(b => b.Text));
        Assert.Contains("Label", flat);
        Assert.Contains("42", flat);
        Assert.Contains("TRUE", flat);
    }

    [Fact]
    public async Task Xls_RowNumbersAreRecorded()
    {
        string path = CreateXlsFile(("Data", ["Row1", "Row2", "Row3"]));
        XlsExtractor extractor = new XlsExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        List<int> rowLines = result.Blocks.Select(b => b.StartLine ?? 0).ToList();
        Assert.Contains(1, rowLines);
        Assert.Contains(2, rowLines);
        Assert.Contains(3, rowLines);
    }

    [Fact]
    public void Xls_CanExtract_MatchesOnlyXls()
    {
        XlsExtractor extractor = new XlsExtractor();
        Assert.True(extractor.CanExtract(DocFormat.Xls));
        Assert.False(extractor.CanExtract(DocFormat.Xlsx));
        Assert.False(extractor.CanExtract(DocFormat.Csv));
        Assert.False(extractor.CanExtract(DocFormat.Doc));
        Assert.False(extractor.CanExtract(DocFormat.Ppt));
    }

    [Fact]
    public void Xlsx_NoLongerClaimsLegacyXls()
    {
        // Prevents regression: legacy .xls must flow to XlsExtractor, not XlsxExtractor.
        XlsxExtractor extractor = new XlsxExtractor();
        Assert.False(extractor.CanExtract(DocFormat.Xls));
    }

    // ── PptExtractor tests ────────────────────────────────────────────────────

    [Fact]
    public async Task Ppt_ExtractsSlideText()
    {
        string path = CreatePptFile(
            ("Introduction slide", false),
            ("Second slide content", false));
        PptExtractor extractor = new PptExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        Assert.NotEmpty(result.Blocks);
        string allText = string.Join(" ", result.Blocks.Select(b => b.Text));
        Assert.Contains("Introduction slide", allText);
        Assert.Contains("Second slide content", allText);
        Assert.Equal(DocFormat.Ppt, result.Metadata.Format);
    }

    [Fact]
    public async Task Ppt_ExtractsJapaneseText()
    {
        string path = CreatePptFile(("日本語プレゼン", false));
        PptExtractor extractor = new PptExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        Assert.NotEmpty(result.Blocks);
        string allText = string.Join(" ", result.Blocks.Select(b => b.Text));
        Assert.Contains("日本語プレゼン", allText);
        Assert.Equal(DocFormat.Ppt, result.Metadata.Format);
    }

    [Fact]
    public async Task Ppt_BlocksHaveSlideNumbers()
    {
        string path = CreatePptFile(
            ("Slide one", false),
            ("Slide two", false),
            ("Slide three", false));
        PptExtractor extractor = new PptExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        Assert.NotEmpty(result.Blocks);
        Assert.All(result.Blocks, b => Assert.NotNull(b.SlideNumber));
        int minSlide = result.Blocks.Min(b => b.SlideNumber!.Value);
        int maxSlide = result.Blocks.Max(b => b.SlideNumber!.Value);
        Assert.Equal(1, minSlide);
        Assert.Equal(3, maxSlide);
    }

    [Fact]
    public async Task Ppt_SlideCountIsReported()
    {
        string path = CreatePptFile(
            ("Slide A", false),
            ("Slide B", false));
        PptExtractor extractor = new PptExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        Assert.Equal(2, result.Metadata.SlideCount);
    }

    [Fact]
    public async Task Ppt_SpeakerNotes_CapturedWithSectionTitle()
    {
        string path = CreatePptFile(
            ("Main slide text", false),
            ("Speaker notes here", true));
        PptExtractor extractor = new PptExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        Assert.NotEmpty(result.Blocks);
        ExtractedBlock? notesBlock = result.Blocks.FirstOrDefault(b =>
            b.SectionTitle != null &&
            b.SectionTitle.Contains("Notes", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(notesBlock);
        Assert.Contains("Speaker notes here", notesBlock!.Text);
    }

    [Fact]
    public async Task Ppt_TitleIsFilenameWithoutExtension()
    {
        string path = CreatePptFile(("Slide content", false));
        PptExtractor extractor = new PptExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        Assert.Equal(Path.GetFileNameWithoutExtension(path), result.Title);
    }

    [Fact]
    public async Task Ppt_EmptyStream_ReturnsEmpty()
    {
        string path = Path.Combine(_tempDir, "empty.ppt");
        using(RootStorage storage = RootStorage.Create(path)) {
            using CfbStream s = storage.CreateStream("PowerPoint Document");
            s.Write(new byte[4]); // less than 8-byte record header minimum
        }

        PptExtractor extractor = new PptExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        Assert.Empty(result.Blocks);
        Assert.Equal(DocFormat.Ppt, result.Metadata.Format);
    }

    [Fact]
    public async Task Ppt_MissingPowerPointDocumentStream_ReturnsEmpty()
    {
        string path = Path.Combine(_tempDir, "no-ppt-stream.ppt");
        using(RootStorage storage = RootStorage.Create(path)) {
            using CfbStream s = storage.CreateStream("SomethingElse");
            s.Write(new byte[16]);
        }

        PptExtractor extractor = new PptExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        Assert.Empty(result.Blocks);
    }

    [Fact]
    public async Task Ppt_Encrypted_ThrowsEncryptedMarker()
    {
        string path = Path.Combine(_tempDir, "encrypted.ppt");
        using(RootStorage storage = RootStorage.Create(path)) {
            using(CfbStream p = storage.CreateStream("PowerPoint Document"))
                p.Write(new byte[16]); // placeholder content
            using(CfbStream e = storage.CreateStream("EncryptedPackage"))
                e.Write(new byte[32]);
        }
        PptExtractor extractor = new PptExtractor();
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => extractor.ExtractAsync(DocumentInput.FromFile(path)));
        Assert.StartsWith("DOC_ENCRYPTED", ex.Message);
    }

    [Fact]
    public void Ppt_CanExtract_MatchesOnlyPptFormat()
    {
        PptExtractor extractor = new PptExtractor();
        Assert.True(extractor.CanExtract(DocFormat.Ppt));
        Assert.False(extractor.CanExtract(DocFormat.Pptx));
        Assert.False(extractor.CanExtract(DocFormat.Doc));
        Assert.False(extractor.CanExtract(DocFormat.Unknown));
    }
}
