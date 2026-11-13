using System.Text;
using Lantern.Core.Extraction;
using Lantern.Infrastructure.Extraction;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using OpenMcdf;

namespace Lantern.IntegrationTests.Extraction;

/// <summary>
/// Tests for legacy Office format extractors (.doc, .xls, .ppt).
/// Creates minimal but valid OLE2 compound files in a temp directory.
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

    // ── Fixture helpers ───────────────────────────────────────────────────────

    /// <summary>Creates a minimal .doc OLE2 file with UTF-16LE paragraphs.</summary>
    private string CreateDocFile(params string[] paragraphs)
    {
        string path = Path.Combine(_tempDir, $"test-{Guid.NewGuid():N}.doc");

        // WordDocument stream: 32-byte FIB placeholder, then UTF-16LE text
        // separated by paragraph marks (CR = 0x000D).
        using MemoryStream ms = new MemoryStream();
        ms.Write(new byte[32], 0, 32); // FIB header placeholder
        foreach(string para in paragraphs) {
            byte[] encoded = Encoding.Unicode.GetBytes(para);
            ms.Write(encoded);
            // paragraph mark \r in UTF-16LE = 0x0D 0x00
            ms.WriteByte(0x0D);
            ms.WriteByte(0x00);
        }

        using RootStorage storage = RootStorage.Create(path);
        using CfbStream wordDocStream = storage.CreateStream("WordDocument");
        byte[] data = ms.ToArray();
        wordDocStream.Write(data, 0, data.Length);

        return path;
    }

    /// <summary>Creates a valid .xls workbook using NPOI HSSF.</summary>
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

    /// <summary>
    /// Creates a minimal .ppt OLE2 file with proper binary record headers.
    /// Each entry is either a slide (false) or a notes block (true).
    /// </summary>
    private string CreatePptFile(params (string text, bool isNotes)[] entries)
    {
        string path = Path.Combine(_tempDir, $"test-{Guid.NewGuid():N}.ppt");

        using MemoryStream ms = new MemoryStream();
        int slideNumber = 0;

        foreach(var (text, isNotes) in entries) {
            if(!isNotes) {
                slideNumber++;
                // SlideAtom (0x03E8) with 24-byte body
                WriteRecordHeader(ms, 0x03E8, 24);
                ms.Write(new byte[24]);
            } else {
                // NotesAtom (0x03F0) with 16-byte body
                WriteRecordHeader(ms, 0x03F0, 16);
                ms.Write(new byte[16]);
            }

            // TextCharsAtom (0x0FA0): UTF-16LE payload
            byte[] textData = Encoding.Unicode.GetBytes(text);
            WriteRecordHeader(ms, 0x0FA0, (uint)textData.Length);
            ms.Write(textData);
        }

        using RootStorage storage = RootStorage.Create(path);
        using CfbStream pptStream = storage.CreateStream("PowerPoint Document");
        byte[] data = ms.ToArray();
        pptStream.Write(data, 0, data.Length);

        return path;
    }

    private static void WriteRecordHeader(Stream s, ushort recType, uint recLen)
    {
        // recVer/recInstance (2 bytes, unused) = 0
        s.WriteByte(0); s.WriteByte(0);
        // recType little-endian
        s.WriteByte((byte)(recType & 0xFF));
        s.WriteByte((byte)(recType >> 8));
        // recLen little-endian (4 bytes)
        s.WriteByte((byte)(recLen & 0xFF));
        s.WriteByte((byte)((recLen >> 8) & 0xFF));
        s.WriteByte((byte)((recLen >> 16) & 0xFF));
        s.WriteByte((byte)(recLen >> 24));
    }

    // ── DocExtractor tests ────────────────────────────────────────────────────

    [Fact]
    public async Task Doc_ExtractsEnglishParagraphs()
    {
        string path = CreateDocFile("Hello World", "Second paragraph", "Third entry");
        DocExtractor extractor = new DocExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        Assert.NotEmpty(result.Blocks);
        string allText = string.Join(" ", result.Blocks.Select(b => b.Text));
        Assert.Contains("Hello World", allText);
        Assert.Contains("Second paragraph", allText);
        Assert.Equal(DocFormat.Doc, result.Metadata.Format);
    }

    [Fact]
    public async Task Doc_ExtractsJapaneseText()
    {
        string path = CreateDocFile("日本語テスト", "二番目の段落");
        DocExtractor extractor = new DocExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        Assert.NotEmpty(result.Blocks);
        string allText = string.Join(" ", result.Blocks.Select(b => b.Text));
        Assert.Contains("日本語テスト", allText);
        Assert.Equal(DocFormat.Doc, result.Metadata.Format);
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
            s.Write(new byte[16]); // less than the 32-byte minimum
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
    public void Doc_CanExtract_MatchesOnlyDocFormat()
    {
        DocExtractor extractor = new DocExtractor();
        Assert.True(extractor.CanExtract(DocFormat.Doc));
        Assert.False(extractor.CanExtract(DocFormat.Docx));
        Assert.False(extractor.CanExtract(DocFormat.Ppt));
        Assert.False(extractor.CanExtract(DocFormat.Unknown));
    }

    // ── XlsxExtractor (XLS mode) tests ───────────────────────────────────────

    [Fact]
    public async Task Xls_ExtractsEnglishRows()
    {
        string path = CreateXlsFile(("Sheet1", ["Alpha", "Beta", "Gamma"]));
        XlsxExtractor extractor = new XlsxExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        Assert.NotEmpty(result.Blocks);
        string allText = string.Join(" ", result.Blocks.Select(b => b.Text));
        Assert.Contains("Alpha", allText);
        Assert.Equal(DocFormat.Xls, result.Metadata.Format);
    }

    [Fact]
    public async Task Xls_ExtractsJapaneseContent()
    {
        string path = CreateXlsFile(("シート1", ["日本語データ", "二行目"]));
        XlsxExtractor extractor = new XlsxExtractor();
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
        XlsxExtractor extractor = new XlsxExtractor();
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
        XlsxExtractor extractor = new XlsxExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        Assert.NotNull(result.Metadata.WorksheetNames);
        Assert.Contains("Alpha", result.Metadata.WorksheetNames!);
        Assert.Contains("Beta", result.Metadata.WorksheetNames!);
    }

    [Fact]
    public void Xls_CanExtract_MatchesXlsAndXlsx()
    {
        XlsxExtractor extractor = new XlsxExtractor();
        Assert.True(extractor.CanExtract(DocFormat.Xls));
        Assert.True(extractor.CanExtract(DocFormat.Xlsx));
        Assert.True(extractor.CanExtract(DocFormat.Csv));
        Assert.False(extractor.CanExtract(DocFormat.Doc));
        Assert.False(extractor.CanExtract(DocFormat.Ppt));
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
    public async Task Ppt_SpeakerNotes_CapturedWithSectionTitle()
    {
        string path = CreatePptFile(
            ("Main slide text", false),
            ("Speaker notes here", true));
        PptExtractor extractor = new PptExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        Assert.NotEmpty(result.Blocks);
        var notesBlock = result.Blocks.FirstOrDefault(b =>
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
            // Wrong stream name — extractor should not throw
            using CfbStream s = storage.CreateStream("SomethingElse");
            s.Write(new byte[16]);
        }

        PptExtractor extractor = new PptExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        Assert.Empty(result.Blocks);
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
