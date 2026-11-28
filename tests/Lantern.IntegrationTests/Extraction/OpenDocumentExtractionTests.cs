using System.IO.Compression;
using System.Text;
using Lantern.Core.Extraction;
using Lantern.Infrastructure.Extraction;

namespace Lantern.IntegrationTests.Extraction;

/// <summary>
/// Tests for OpenDocument Format extractors (.odt/.ods/.odp).
///
/// Fixtures are built as in-process ZIP archives that assemble a minimal ODF
/// package: the uncompressed <c>mimetype</c> stream plus a hand-authored
/// <c>content.xml</c>. This exercises the real <see cref="OdfPackageReader"/>
/// and the XML parsing paths of each extractor without pulling in LibreOffice.
/// </summary>
public sealed class OpenDocumentExtractionTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"lantern-odf-{Guid.NewGuid():N}");

    public OpenDocumentExtractionTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ── Package builders ─────────────────────────────────────────────────────

    private string CreatePackage(string extension, string mimeType, string contentXml,
        string? metaXml = null, string? manifestXml = null)
    {
        string path = Path.Combine(_tempDir, $"test-{Guid.NewGuid():N}{extension}");
        using(FileStream fs = File.Create(path))
        using(ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create)) {
            // The mimetype entry must be the first entry and stored (not deflated)
            // per the ODF specification. ZipArchiveMode.Create accepts a
            // CompressionLevel.NoCompression hint.
            ZipArchiveEntry mt = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using(Stream s = mt.Open()) {
                byte[] bytes = Encoding.ASCII.GetBytes(mimeType);
                s.Write(bytes, 0, bytes.Length);
            }

            WriteEntry(zip, "content.xml", contentXml);
            if(metaXml != null) WriteEntry(zip, "meta.xml", metaXml);
            if(manifestXml != null) WriteEntry(zip, "META-INF/manifest.xml", manifestXml);
        }
        return path;
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using Stream s = e.Open();
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        s.Write(bytes, 0, bytes.Length);
    }

    private static string OdtContent(string body) => $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<office:document-content
    xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0""
    xmlns:text=""urn:oasis:names:tc:opendocument:xmlns:text:1.0""
    xmlns:table=""urn:oasis:names:tc:opendocument:xmlns:table:1.0"">
  <office:body>
    <office:text>
{body}
    </office:text>
  </office:body>
</office:document-content>";

    private static string OdsContent(string body) => $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<office:document-content
    xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0""
    xmlns:text=""urn:oasis:names:tc:opendocument:xmlns:text:1.0""
    xmlns:table=""urn:oasis:names:tc:opendocument:xmlns:table:1.0"">
  <office:body>
    <office:spreadsheet>
{body}
    </office:spreadsheet>
  </office:body>
</office:document-content>";

    private static string OdpContent(string body) => $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<office:document-content
    xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0""
    xmlns:text=""urn:oasis:names:tc:opendocument:xmlns:text:1.0""
    xmlns:draw=""urn:oasis:names:tc:opendocument:xmlns:drawing:1.0""
    xmlns:presentation=""urn:oasis:names:tc:opendocument:xmlns:presentation:1.0"">
  <office:body>
    <office:presentation>
{body}
    </office:presentation>
  </office:body>
</office:document-content>";

    // ── ODT tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Odt_ExtractsEnglishParagraphsAndHeadings()
    {
        string content = OdtContent(@"
      <text:h text:outline-level=""1"">Introduction</text:h>
      <text:p>First paragraph of body text.</text:p>
      <text:h text:outline-level=""2"">Details</text:h>
      <text:p>Second paragraph with <text:span>inline</text:span> markup.</text:p>");
        string path = CreatePackage(".odt", OdfPackageReader.MimeOdt, content);

        OdtExtractor extractor = new OdtExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        List<ExtractedBlock> headings = result.Blocks.Where(b => b.Type == BlockType.Heading).ToList();
        List<ExtractedBlock> paragraphs = result.Blocks.Where(b => b.Type == BlockType.Paragraph).ToList();

        Assert.Equal(2, headings.Count);
        Assert.Equal("Introduction", headings[0].Text);
        Assert.Equal(1, headings[0].HeadingLevel);
        Assert.Equal("Details", headings[1].Text);
        Assert.Equal(2, headings[1].HeadingLevel);
        Assert.Equal(2, paragraphs.Count);
        Assert.Contains("First paragraph", paragraphs[0].Text);
        Assert.Contains("inline", paragraphs[1].Text);
        Assert.Equal(DocFormat.Odt, result.Metadata.Format);
    }

    [Fact]
    public async Task Odt_ExtractsJapaneseText()
    {
        string content = OdtContent(@"
      <text:h text:outline-level=""1"">日本語の見出し</text:h>
      <text:p>これは本文です。</text:p>");
        string path = CreatePackage(".odt", OdfPackageReader.MimeOdt, content);

        OdtExtractor extractor = new OdtExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        string flat = string.Join(" ", result.Blocks.Select(b => b.Text));
        Assert.Contains("日本語の見出し", flat);
        Assert.Contains("これは本文です。", flat);
    }

    [Fact]
    public async Task Odt_PreservesStructuralWhitespace()
    {
        string content = OdtContent(@"
      <text:p>A<text:s text:c=""3""/>B<text:tab/>C<text:line-break/>D</text:p>");
        string path = CreatePackage(".odt", OdfPackageReader.MimeOdt, content);

        OdtExtractor extractor = new OdtExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        ExtractedBlock block = Assert.Single(result.Blocks);
        Assert.Contains("A   B", block.Text); // text:s c="3" expands to three spaces
        Assert.Contains("\t", block.Text);
        Assert.Contains("D", block.Text);
    }

    [Fact]
    public async Task Odt_ExtractsTablesAsSingleBlock()
    {
        string content = OdtContent(@"
      <text:p>Lead paragraph.</text:p>
      <table:table>
        <table:table-row>
          <table:table-cell><text:p>A1</text:p></table:table-cell>
          <table:table-cell><text:p>B1</text:p></table:table-cell>
        </table:table-row>
        <table:table-row>
          <table:table-cell><text:p>A2</text:p></table:table-cell>
          <table:table-cell><text:p>B2</text:p></table:table-cell>
        </table:table-row>
      </table:table>");
        string path = CreatePackage(".odt", OdfPackageReader.MimeOdt, content);

        OdtExtractor extractor = new OdtExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        ExtractedBlock? tableBlock = result.Blocks.FirstOrDefault(b => b.Type == BlockType.Table);
        Assert.NotNull(tableBlock);
        Assert.Contains("A1", tableBlock!.Text);
        Assert.Contains("B1", tableBlock.Text);
        Assert.Contains("A2", tableBlock.Text);
    }

    [Fact]
    public async Task Odt_UsesMetaTitleWhenAvailable()
    {
        string content = OdtContent(@"<text:p>Body only.</text:p>");
        string meta = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<office:document-meta xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0""
    xmlns:dc=""http://purl.org/dc/elements/1.1/""
    xmlns:meta=""urn:oasis:names:tc:opendocument:xmlns:meta:1.0"">
  <office:meta>
    <dc:title>Quarterly Report</dc:title>
    <meta:initial-creator>Alice</meta:initial-creator>
  </office:meta>
</office:document-meta>";
        string path = CreatePackage(".odt", OdfPackageReader.MimeOdt, content, metaXml: meta);

        OdtExtractor extractor = new OdtExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        Assert.Equal("Quarterly Report", result.Title);
        Assert.Equal("Alice", result.Metadata.Author);
    }

    [Fact]
    public async Task Odt_RejectsNonTextMimeType()
    {
        string content = OdsContent(@"<table:table table:name=""Sheet1""/>");
        string path = CreatePackage(".odt", OdfPackageReader.MimeOds, content);

        OdtExtractor extractor = new OdtExtractor();
        await Assert.ThrowsAsync<NotSupportedException>(
            () => extractor.ExtractAsync(DocumentInput.FromFile(path)));
    }

    [Fact]
    public async Task Odt_EncryptedManifest_RaisesEncryptedException()
    {
        string content = OdtContent(@"<text:p>ignored</text:p>");
        string manifest = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<manifest:manifest xmlns:manifest=""urn:oasis:names:tc:opendocument:xmlns:manifest:1.0"">
  <manifest:file-entry manifest:full-path=""content.xml"" manifest:media-type=""text/xml"">
    <manifest:encryption-data>
      <manifest:algorithm manifest:algorithm-name=""Blowfish CFB""/>
    </manifest:encryption-data>
  </manifest:file-entry>
</manifest:manifest>";
        string path = CreatePackage(".odt", OdfPackageReader.MimeOdt, content, manifestXml: manifest);

        OdtExtractor extractor = new OdtExtractor();
        await Assert.ThrowsAsync<OdfEncryptedPackageException>(
            () => extractor.ExtractAsync(DocumentInput.FromFile(path)));
    }

    [Fact]
    public async Task Odt_MissingMimeType_RaisesUnsupported()
    {
        string path = Path.Combine(_tempDir, $"no-mime-{Guid.NewGuid():N}.odt");
        using(FileStream fs = File.Create(path))
        using(ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create))
            WriteEntry(zip, "content.xml", OdtContent("<text:p>x</text:p>"));

        OdtExtractor extractor = new OdtExtractor();
        await Assert.ThrowsAsync<OdfUnsupportedKindException>(
            () => extractor.ExtractAsync(DocumentInput.FromFile(path)));
    }

    [Fact]
    public async Task Odt_InvalidZip_RaisesMalformed()
    {
        string path = Path.Combine(_tempDir, $"invalid-{Guid.NewGuid():N}.odt");
        File.WriteAllText(path, "not a zip at all");

        OdtExtractor extractor = new OdtExtractor();
        await Assert.ThrowsAsync<OdfMalformedPackageException>(
            () => extractor.ExtractAsync(DocumentInput.FromFile(path)));
    }

    [Fact]
    public void Odt_CanExtract_MatchesOnlyOdt()
    {
        OdtExtractor extractor = new OdtExtractor();
        Assert.True(extractor.CanExtract(DocFormat.Odt));
        Assert.False(extractor.CanExtract(DocFormat.Ods));
        Assert.False(extractor.CanExtract(DocFormat.Odp));
        Assert.False(extractor.CanExtract(DocFormat.Docx));
    }

    // ── ODS tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ods_ExtractsMultipleWorksheets()
    {
        string content = OdsContent(@"
      <table:table table:name=""Summary"">
        <table:table-row>
          <table:table-cell office:value-type=""string"" xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0""><text:p>Name</text:p></table:table-cell>
          <table:table-cell office:value-type=""string"" xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0""><text:p>Value</text:p></table:table-cell>
        </table:table-row>
        <table:table-row>
          <table:table-cell office:value-type=""string"" xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0""><text:p>Revenue</text:p></table:table-cell>
          <table:table-cell office:value-type=""float"" office:value=""1234.5"" xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0""><text:p>1234.5</text:p></table:table-cell>
        </table:table-row>
      </table:table>
      <table:table table:name=""Details"">
        <table:table-row>
          <table:table-cell office:value-type=""string"" xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0""><text:p>Row A</text:p></table:table-cell>
        </table:table-row>
      </table:table>");
        string path = CreatePackage(".ods", OdfPackageReader.MimeOds, content);

        OdsExtractor extractor = new OdsExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        Assert.Equal(2, result.Blocks.Count);
        Assert.Contains("Summary", result.Metadata.WorksheetNames!);
        Assert.Contains("Details", result.Metadata.WorksheetNames!);
        ExtractedBlock summary = result.Blocks.First(b => b.SheetName == "Summary");
        Assert.Contains("Revenue", summary.Text);
        Assert.Contains("1234.5", summary.Text);
        Assert.Equal(DocFormat.Ods, result.Metadata.Format);
    }

    [Fact]
    public async Task Ods_HandlesJapaneseContent()
    {
        string content = OdsContent(@"
      <table:table table:name=""集計"">
        <table:table-row>
          <table:table-cell office:value-type=""string"" xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0""><text:p>名前</text:p></table:table-cell>
          <table:table-cell office:value-type=""string"" xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0""><text:p>日本語データ</text:p></table:table-cell>
        </table:table-row>
      </table:table>");
        string path = CreatePackage(".ods", OdfPackageReader.MimeOds, content);

        OdsExtractor extractor = new OdsExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        ExtractedBlock sheet = Assert.Single(result.Blocks);
        Assert.Equal("集計", sheet.SheetName);
        Assert.Contains("名前", sheet.Text);
        Assert.Contains("日本語データ", sheet.Text);
    }

    [Fact]
    public async Task Ods_RepeatedCellsAreBounded()
    {
        // 2^31 columns-repeated would blow up a naive extractor. The extractor
        // caps the per-row width internally, so it must not throw and must
        // not emit anywhere close to that many cells.
        string content = OdsContent(@"
      <table:table table:name=""Bomb"">
        <table:table-row>
          <table:table-cell office:value-type=""string""
              table:number-columns-repeated=""2147483647""
              xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0"">
            <text:p>X</text:p>
          </table:table-cell>
        </table:table-row>
      </table:table>");
        string path = CreatePackage(".ods", OdfPackageReader.MimeOds, content);

        OdsExtractor extractor = new OdsExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        ExtractedBlock sheet = Assert.Single(result.Blocks);
        // Output should be bounded (well under a MB).
        Assert.True(sheet.Text.Length < 32 * 1024,
            $"Sheet text is {sheet.Text.Length} chars — expected bounded output");
    }

    [Fact]
    public async Task Ods_EmptyRowsDoNotEmitLines()
    {
        string content = OdsContent(@"
      <table:table table:name=""Sparse"">
        <table:table-row table:number-rows-repeated=""100""/>
        <table:table-row>
          <table:table-cell office:value-type=""string"" xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0""><text:p>Hello</text:p></table:table-cell>
        </table:table-row>
      </table:table>");
        string path = CreatePackage(".ods", OdfPackageReader.MimeOds, content);

        OdsExtractor extractor = new OdsExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        ExtractedBlock sheet = Assert.Single(result.Blocks);
        // Row number should reflect the logical position after the repeated empty rows.
        Assert.Contains("R101:", sheet.Text);
        Assert.Contains("Hello", sheet.Text);
        Assert.DoesNotContain("R1:", sheet.Text);
    }

    [Fact]
    public void Ods_CanExtract_MatchesOnlyOds()
    {
        OdsExtractor extractor = new OdsExtractor();
        Assert.True(extractor.CanExtract(DocFormat.Ods));
        Assert.False(extractor.CanExtract(DocFormat.Odt));
        Assert.False(extractor.CanExtract(DocFormat.Xlsx));
    }

    // ── ODP tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Odp_ExtractsSlidesWithTitlesAndNotes()
    {
        string content = OdpContent(@"
      <draw:page draw:name=""Slide1"">
        <draw:frame presentation:class=""title"">
          <draw:text-box><text:p>Welcome</text:p></draw:text-box>
        </draw:frame>
        <draw:frame>
          <draw:text-box>
            <text:p>Body line one</text:p>
            <text:p>Body line two</text:p>
          </draw:text-box>
        </draw:frame>
        <presentation:notes>
          <draw:frame>
            <draw:text-box><text:p>Remember to introduce the team.</text:p></draw:text-box>
          </draw:frame>
        </presentation:notes>
      </draw:page>
      <draw:page draw:name=""Slide2"">
        <draw:frame presentation:class=""title"">
          <draw:text-box><text:p>日本語スライド</text:p></draw:text-box>
        </draw:frame>
        <draw:frame>
          <draw:text-box><text:p>本文テキスト</text:p></draw:text-box>
        </draw:frame>
      </draw:page>");
        string path = CreatePackage(".odp", OdfPackageReader.MimeOdp, content);

        OdpExtractor extractor = new OdpExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        Assert.Equal(2, result.Metadata.SlideCount);

        List<ExtractedBlock> slide1 = result.Blocks.Where(b => b.SlideNumber == 1).ToList();
        Assert.Contains(slide1, b => b.Type == BlockType.Heading && b.Text == "Welcome");
        Assert.Contains(slide1, b => b.Text.Contains("Body line one") && b.Text.Contains("Body line two"));
        ExtractedBlock notes = Assert.Single(slide1, b => b.SectionTitle != null && b.SectionTitle.Contains("Notes"));
        Assert.Contains("Remember to introduce the team.", notes.Text);

        List<ExtractedBlock> slide2 = result.Blocks.Where(b => b.SlideNumber == 2).ToList();
        Assert.Contains(slide2, b => b.Text == "日本語スライド");
        Assert.Contains(slide2, b => b.Text.Contains("本文テキスト"));
    }

    [Fact]
    public async Task Odp_PreservesSlideOrder()
    {
        string content = OdpContent(@"
      <draw:page><draw:frame><draw:text-box><text:p>One</text:p></draw:text-box></draw:frame></draw:page>
      <draw:page><draw:frame><draw:text-box><text:p>Two</text:p></draw:text-box></draw:frame></draw:page>
      <draw:page><draw:frame><draw:text-box><text:p>Three</text:p></draw:text-box></draw:frame></draw:page>");
        string path = CreatePackage(".odp", OdfPackageReader.MimeOdp, content);

        OdpExtractor extractor = new OdpExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(DocumentInput.FromFile(path));

        List<int?> slideNumbers = result.Blocks.Select(b => b.SlideNumber).Distinct().ToList();
        Assert.Equal(new int?[] { 1, 2, 3 }, slideNumbers);
        List<string> texts = result.Blocks.Select(b => b.Text).ToList();
        Assert.Contains("One", texts);
        Assert.Contains("Two", texts);
        Assert.Contains("Three", texts);
    }

    [Fact]
    public void Odp_CanExtract_MatchesOnlyOdp()
    {
        OdpExtractor extractor = new OdpExtractor();
        Assert.True(extractor.CanExtract(DocFormat.Odp));
        Assert.False(extractor.CanExtract(DocFormat.Odt));
        Assert.False(extractor.CanExtract(DocFormat.Pptx));
    }

    // ── Shared package reader tests ──────────────────────────────────────────

    [Fact]
    public void PackageReader_RejectsAbsoluteEntryPaths()
    {
        string path = Path.Combine(_tempDir, $"traversal-{Guid.NewGuid():N}.odt");
        using(FileStream fs = File.Create(path))
        using(ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create)) {
            ZipArchiveEntry mt = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using(Stream s = mt.Open()) {
                byte[] bytes = Encoding.ASCII.GetBytes(OdfPackageReader.MimeOdt);
                s.Write(bytes, 0, bytes.Length);
            }
            WriteEntry(zip, "../evil.xml", "<x/>");
        }

        Assert.Throws<OdfMalformedPackageException>(
            () => OdfPackageReader.Open(DocumentInput.FromFile(path)));
    }
}
