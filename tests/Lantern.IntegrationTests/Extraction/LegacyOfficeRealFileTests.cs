using Lantern.Core.Extraction;
using Lantern.Infrastructure.Extraction;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lantern.IntegrationTests.Extraction;

/// <summary>
/// Extraction tests that run against real Office 97–2003 binary fixtures in
/// <c>tests/Lantern.IntegrationTests/data</c>. Unlike the synthetic fixtures
/// in <see cref="LegacyOfficeExtractionTests"/>, these files were produced by
/// Microsoft Office and exercise the parsers against the full range of records
/// a real document actually writes.
/// </summary>
public sealed class LegacyOfficeRealFileTests
{
    private static readonly string DataDir =
        Path.Combine(AppContext.BaseDirectory, "data");

    private static string FixturePath(string name) => Path.Combine(DataDir, name);

    private static DocumentInput Input(string name) => new() {
        FilePath = FixturePath(name),
        FileName = name,
        Extension = Path.GetExtension(name)
    };

    // ── .doc ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Doc_RealFixture_ExtractsExpectedBlockCount()
    {
        ExtractedDocument doc = await new DocExtractor().ExtractAsync(Input("テスト.doc"));

        Assert.Equal("テスト", doc.Title);
        Assert.Equal(DocFormat.Doc, doc.Metadata.Format);
        Assert.InRange(doc.Blocks.Count, 10, 40);
        Assert.All(doc.Blocks, b => Assert.Equal(BlockType.Paragraph, b.Type));
    }

    [Fact]
    public async Task Doc_RealFixture_ExtractsJapaneseContent()
    {
        ExtractedDocument doc = await new DocExtractor().ExtractAsync(Input("テスト.doc"));

        string all = string.Join("\n", doc.Blocks.Select(b => b.Text));

        // Title paragraph from the real template.
        Assert.Contains("Word", all);
        Assert.Contains("Copilot", all);
        Assert.Contains("プロンプト", all);
        // Japanese body paragraphs that appear verbatim in the file.
        Assert.Contains("はじめに", all);
        Assert.Contains("旅行日程を作成する", all);
        Assert.Contains("添え状の下書きを作成する", all);
        Assert.Contains("その他のリソース", all);
    }

    [Fact]
    public async Task Doc_RealFixture_ParagraphsAreNormalizedAndNonEmpty()
    {
        ExtractedDocument doc = await new DocExtractor().ExtractAsync(Input("テスト.doc"));

        Assert.All(doc.Blocks, b => {
            Assert.False(string.IsNullOrWhiteSpace(b.Text));
            Assert.DoesNotContain('\u0000', b.Text);
            // Word's embedded control markers (0x13 FIELDBEGIN, 0x14 FIELDSEP,
            // 0x15 FIELDEND, 0x07 cell marker) must not leak into chunks.
            Assert.DoesNotContain('\u0013', b.Text);
            Assert.DoesNotContain('\u0014', b.Text);
            Assert.DoesNotContain('\u0015', b.Text);
            Assert.DoesNotContain('\u0007', b.Text);
        });
    }

    // ── .xls ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Xls_RealFixture_ExtractsAllThreeWorksheets()
    {
        ExtractedDocument doc = await new XlsExtractor().ExtractAsync(Input("テスト.xls"));

        Assert.Equal("テスト", doc.Title);
        Assert.Equal(DocFormat.Xls, doc.Metadata.Format);
        Assert.NotNull(doc.Metadata.WorksheetNames);
        Assert.Equal(
            new[] { "Start", "Gift budget and tracker", "Data" },
            doc.Metadata.WorksheetNames!.ToArray());
    }

    [Fact]
    public async Task Xls_RealFixture_RowsHaveSheetMetadataAndLineNumbers()
    {
        ExtractedDocument doc = await new XlsExtractor().ExtractAsync(Input("テスト.xls"));

        Assert.All(doc.Blocks, b => {
            Assert.Equal(BlockType.Table, b.Type);
            Assert.False(string.IsNullOrEmpty(b.SheetName));
            Assert.Equal(b.SheetName, b.SectionTitle);
            Assert.NotNull(b.StartLine);
            Assert.True(b.StartLine!.Value >= 1);
            Assert.Equal(b.StartLine, b.EndLine);
        });

        // Each worksheet should produce at least one row.
        Assert.Contains(doc.Blocks, b => b.SheetName == "Start");
        Assert.Contains(doc.Blocks, b => b.SheetName == "Gift budget and tracker");
        Assert.Contains(doc.Blocks, b => b.SheetName == "Data");
    }

    [Fact]
    public async Task Xls_RealFixture_CellValuesAreExtractedAcrossSheets()
    {
        ExtractedDocument doc = await new XlsExtractor().ExtractAsync(Input("テスト.xls"));

        string start = Concat(doc.Blocks.Where(b => b.SheetName == "Start"));
        string tracker = Concat(doc.Blocks.Where(b => b.SheetName == "Gift budget and tracker"));
        string data = Concat(doc.Blocks.Where(b => b.SheetName == "Data"));

        // "Start" worksheet narrative.
        Assert.Contains("ABOUT THE TEMPLATE", start);
        Assert.Contains("GIFT BUDGET AND TRACKER", start);

        // "Gift budget and tracker" table rows and headers.
        Assert.Contains("GIFT BUDGET AND TRACKER", tracker);
        Assert.Contains("MONTHLY OVERVIEW", tracker);
        Assert.Contains("Occasion", tracker);
        Assert.Contains("Laura", tracker);
        Assert.Contains("Birthdays", tracker);
        Assert.Contains("Headphones", tracker);
        Assert.Contains("Crystal vase", tracker);
        // Numeric cells must round-trip through DataFormatter as text.
        Assert.Contains("$50", tracker);
        Assert.Contains("$150", tracker);

        // "Data" worksheet lookup table.
        Assert.Contains("Occasions", data);
        Assert.Contains("January", data);
        Assert.Contains("December", data);
    }

    [Fact]
    public async Task Xls_RealFixture_TabSeparatesCellsWithinARow()
    {
        ExtractedDocument doc = await new XlsExtractor().ExtractAsync(Input("テスト.xls"));

        // The row "Laura Birthdays June $50 $30 Shoes ..." lives in one block,
        // produced by joining cell texts with a tab character.
        ExtractedBlock lauraRow = doc.Blocks.Single(b => b.Text.Contains("Laura"));
        Assert.Contains('\t', lauraRow.Text);
        Assert.Contains("Laura\tBirthdays\tJune", lauraRow.Text);
    }

    // ── .ppt ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ppt_RealFixture_ReportsTwoSlides()
    {
        ExtractedDocument doc = await new PptExtractor().ExtractAsync(Input("テスト.ppt"));

        Assert.Equal("テスト", doc.Title);
        Assert.Equal(DocFormat.Ppt, doc.Metadata.Format);
        Assert.Equal(2, doc.Metadata.SlideCount);
        Assert.All(doc.Blocks, b => Assert.Equal(BlockType.Paragraph, b.Type));
        Assert.All(doc.Blocks, b => Assert.NotNull(b.SlideNumber));
    }

    [Fact]
    public async Task Ppt_RealFixture_SlideOneContainsCompanyBrandingAndContact()
    {
        ExtractedDocument doc = await new PptExtractor().ExtractAsync(Input("テスト.ppt"));

        string slide1 = Concat(doc.Blocks.Where(b => b.SlideNumber == 1));

        Assert.Contains("マージー旅行社", slide1);
        Assert.Contains("4567 Main Street", slide1);
        Assert.Contains("Raleigh, NC 02134", slide1);
        Assert.Contains("718.555.1234", slide1);
        Assert.Contains("www.margiestravel.com", slide1);
    }

    [Fact]
    public async Task Ppt_RealFixture_SlideTwoContainsServiceDescriptionAndTestimonial()
    {
        ExtractedDocument doc = await new PptExtractor().ExtractAsync(Input("テスト.ppt"));

        string slide2 = Concat(doc.Blocks.Where(b => b.SlideNumber == 2));

        Assert.Contains("パスポート", slide2);
        Assert.Contains("ホテル", slide2);
        Assert.Contains("旅行保険", slide2);
        Assert.Contains("Henriette Andersen", slide2);
        Assert.Contains("Northwind Traders", slide2);
        Assert.Contains("Contoso Limited", slide2);
    }

    [Fact]
    public async Task Ppt_RealFixture_DoesNotEmitMasterSlidePlaceholders()
    {
        // The template's master slide carries the Latin "Click to edit Master"
        // / Japanese "マスター" placeholders. Those must be suppressed by the
        // InMaster guard so they do not pollute real slide chunks.
        ExtractedDocument doc = await new PptExtractor().ExtractAsync(Input("テスト.ppt"));

        string all = Concat(doc.Blocks);
        Assert.DoesNotContain("Click to edit Master", all);
        Assert.DoesNotContain("マスター タイトル", all);
    }

    // ── Dispatcher wiring ────────────────────────────────────────────────────

    [Fact]
    public async Task Dispatcher_RoutesDocXlsPptToDedicatedExtractors()
    {
        // The dispatcher must pick the correct extractor by format — if .xls
        // regressed to XlsxExtractor the call would throw, and if .ppt
        // regressed it would produce zero blocks.
        DocumentExtractionDispatcher dispatcher = new DocumentExtractionDispatcher(
            new IDocumentExtractor[] { new DocExtractor(), new XlsExtractor(), new PptExtractor() },
            new DocumentFormatDetector(),
            NullLogger<DocumentExtractionDispatcher>.Instance);

        ExtractedDocument doc = await dispatcher.ExtractAsync(Input("テスト.doc"));
        ExtractedDocument xls = await dispatcher.ExtractAsync(Input("テスト.xls"));
        ExtractedDocument ppt = await dispatcher.ExtractAsync(Input("テスト.ppt"));

        Assert.Equal(DocFormat.Doc, doc.Metadata.Format);
        Assert.Equal(DocFormat.Xls, xls.Metadata.Format);
        Assert.Equal(DocFormat.Ppt, ppt.Metadata.Format);
        Assert.NotEmpty(doc.Blocks);
        Assert.NotEmpty(xls.Blocks);
        Assert.NotEmpty(ppt.Blocks);
    }

    private static string Concat(IEnumerable<ExtractedBlock> blocks) =>
        string.Join("\n", blocks.Select(b => b.Text));
}
