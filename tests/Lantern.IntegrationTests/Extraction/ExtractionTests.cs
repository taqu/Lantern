using Lantern.Core.Extraction;
using Lantern.Core.Search;
using Lantern.Infrastructure.Extraction;
using Lantern.Infrastructure.Search;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lantern.IntegrationTests.Extraction;

/// <summary>
/// Extraction tests using real fixture files from samples/documents/.
/// These tests run without Meilisearch or embedding server.
/// </summary>
public sealed class ExtractionTests
{
    private static string? FindSamplesDir()
    {
        string? dir = Directory.GetCurrentDirectory();
        for (int i = 0; i < 8; i++)
        {
            if (dir == null) break;
            string candidate = Path.Combine(dir, "samples", "documents");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static string? GetSamplePath(string filename)
    {
        string? dir = FindSamplesDir();
        if (dir == null) return null;
        string path = Path.Combine(dir, filename);
        return File.Exists(path) ? path : null;
    }

    private static StructureAwareChunker CreateChunker()
    {
        IOptions<ChunkingOptions> options = Options.Create(new ChunkingOptions { TargetTokens = 512, MaxTokens = 768, OverlapTokens = 64 });
        var textChunker = new TextChunkingService(options);
        return new StructureAwareChunker(textChunker, options);
    }

    [Fact]
    public async Task PlainText_ExtractsContent()
    {
        string? path = GetSamplePath("sample.txt");
        if (path == null) return; // Skip if no fixture

        var extractor = new PlainTextExtractor();
        var input = DocumentInput.FromFile(path);
        ExtractedDocument result = await extractor.ExtractAsync(input);

        Assert.NotEmpty(result.Blocks);
        Assert.True(result.Blocks.Sum(b => b.Text.Length) > 0);
        Assert.Equal(DocFormat.PlainText, result.Metadata.Format);
    }

    [Fact]
    public async Task PlainText_HasTitle()
    {
        string? path = GetSamplePath("sample.txt");
        if (path == null) return;

        var extractor = new PlainTextExtractor();
        var input = DocumentInput.FromFile(path);
        ExtractedDocument result = await extractor.ExtractAsync(input);

        Assert.NotEmpty(result.Title);
        Assert.Equal("sample", result.Title); // filename without extension
    }

    [Fact]
    public async Task Html_ExtractsTextWithoutMarkup()
    {
        string? path = GetSamplePath("sample.html");
        if (path == null) return;

        var extractor = new HtmlExtractor();
        var input = DocumentInput.FromFile(path);
        ExtractedDocument result = await extractor.ExtractAsync(input);

        Assert.NotEmpty(result.Blocks);
        string allText = string.Join(" ", result.Blocks.Select(b => b.Text));
        Assert.DoesNotContain("<script", allText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<style", allText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(DocFormat.Html, result.Metadata.Format);
    }

    [Fact]
    public async Task Docx_ExtractsContentWithBlocks()
    {
        string? path = GetSamplePath("sample.docx");
        if (path == null) return;

        var extractor = new DocxExtractor();
        var input = DocumentInput.FromFile(path);
        ExtractedDocument result = await extractor.ExtractAsync(input);

        Assert.NotEmpty(result.Blocks);
        Assert.True(result.Blocks.Sum(b => b.Text.Length) > 0);
        Assert.Equal(DocFormat.Docx, result.Metadata.Format);
    }

    [Fact]
    public async Task Xlsx_ExtractsSheets()
    {
        string? path = GetSamplePath("sample.xlsx");
        if (path == null) return;

        var extractor = new XlsxExtractor();
        var input = DocumentInput.FromFile(path);
        ExtractedDocument result = await extractor.ExtractAsync(input);

        Assert.NotEmpty(result.Blocks);
        Assert.NotNull(result.Metadata.WorksheetNames);
        Assert.NotEmpty(result.Metadata.WorksheetNames);
        Assert.All(result.Blocks, b => Assert.NotNull(b.SheetName));
        Assert.Equal(DocFormat.Xlsx, result.Metadata.Format);
    }

    [Fact]
    public async Task Pptx_ExtractsSlides()
    {
        string? path = GetSamplePath("sample.pptx");
        if (path == null) return;

        var extractor = new PptxExtractor();
        var input = DocumentInput.FromFile(path);
        ExtractedDocument result = await extractor.ExtractAsync(input);

        Assert.NotEmpty(result.Blocks);
        Assert.Equal(DocFormat.Pptx, result.Metadata.Format);
        var chunksWithSlide = result.Blocks.Where(b => b.SlideNumber.HasValue).ToList();
        if (chunksWithSlide.Any())
            Assert.All(chunksWithSlide, b => Assert.True(b.SlideNumber > 0));
    }

    [Fact]
    public async Task Pdf_Skipped_NoFixture()
    {
        // No sample.pdf exists - this verifies PDF extractor is registered
        // but we skip the actual test since no fixture is available.
        var extractor = new PdfExtractor();
        Assert.True(extractor.CanExtract(DocFormat.Pdf));
        Assert.False(extractor.CanExtract(DocFormat.Docx));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task StructureAwareChunker_ProducesChunksFromTxt()
    {
        string? path = GetSamplePath("sample.txt");
        if (path == null) return;

        var extractor = new PlainTextExtractor();
        var input = DocumentInput.FromFile(path);
        ExtractedDocument extracted = await extractor.ExtractAsync(input);

        StructureAwareChunker chunker = CreateChunker();
        IReadOnlyList<SearchChunk> chunks = chunker.Chunk("doc-id", "Test Document", "file:///test.txt", ".txt", "test-source", "file", extracted.Blocks);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c =>
        {
            Assert.NotEmpty(c.Id);
            Assert.NotEmpty(c.Content);
            Assert.Equal("doc-id", c.DocumentId);
        });
        // Chunk indices should be sequential
        for (int i = 0; i < chunks.Count; i++)
            Assert.Equal(i, chunks[i].ChunkIndex);
    }

    [Fact]
    public async Task StructureAwareChunker_ProducesChunksFromDocx()
    {
        string? path = GetSamplePath("sample.docx");
        if (path == null) return;

        var extractor = new DocxExtractor();
        var input = DocumentInput.FromFile(path);
        ExtractedDocument extracted = await extractor.ExtractAsync(input);

        StructureAwareChunker chunker = CreateChunker();
        IReadOnlyList<SearchChunk> chunks = chunker.Chunk("test-doc-id", "Test Document", "file:///test.docx", ".docx", "test-source", "file", extracted.Blocks);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c =>
        {
            Assert.NotEmpty(c.Id);
            Assert.NotEmpty(c.Content);
            Assert.Equal("test-doc-id", c.DocumentId);
        });
        for (int i = 0; i < chunks.Count; i++)
            Assert.Equal(i, chunks[i].ChunkIndex);
    }

    [Fact]
    public async Task Pptx_ChunksPreserveSlideNumbers()
    {
        string? path = GetSamplePath("sample.pptx");
        if (path == null) return;

        var extractor = new PptxExtractor();
        var input = DocumentInput.FromFile(path);
        ExtractedDocument extracted = await extractor.ExtractAsync(input);

        StructureAwareChunker chunker = CreateChunker();
        IReadOnlyList<SearchChunk> chunks = chunker.Chunk("doc-id", "Presentation", "file:///test.pptx", ".pptx", "src", "file", extracted.Blocks);

        var chunksWithSlide = chunks.Where(c => c.SlideNumber.HasValue).ToList();
        if (chunksWithSlide.Any())
            Assert.All(chunksWithSlide, c => Assert.True(c.SlideNumber > 0));
    }

    [Fact]
    public async Task Xlsx_ChunksPreserveSheetNames()
    {
        string? path = GetSamplePath("sample.xlsx");
        if (path == null) return;

        var extractor = new XlsxExtractor();
        var input = DocumentInput.FromFile(path);
        ExtractedDocument extracted = await extractor.ExtractAsync(input);

        StructureAwareChunker chunker = CreateChunker();
        IReadOnlyList<SearchChunk> chunks = chunker.Chunk("doc-id", "Spreadsheet", "file:///test.xlsx", ".xlsx", "src", "file", extracted.Blocks);

        if (chunks.Any())
            Assert.All(chunks, c => Assert.NotNull(c.SheetName));
    }

    [Fact]
    public async Task DocumentDispatcher_SelectsCorrectExtractor()
    {
        var extractors = new IDocumentExtractor[]
        {
            new PlainTextExtractor(), new MarkdownExtractor(), new HtmlExtractor(),
            new DocxExtractor(), new XlsxExtractor(), new PptxExtractor()
        };
        var detector = new DocumentFormatDetector();
        var dispatcher = new DocumentExtractionDispatcher(extractors, detector, NullLogger<DocumentExtractionDispatcher>.Instance);

        string? path = GetSamplePath("sample.txt");
        if (path == null) return;

        var input = DocumentInput.FromFile(path);
        ExtractedDocument result = await dispatcher.ExtractAsync(input);

        Assert.NotEmpty(result.Blocks);
    }

    [Fact]
    public async Task DocumentDispatcher_ThrowsForUnsupported()
    {
        var extractors = new IDocumentExtractor[] { new PlainTextExtractor() };
        var detector = new DocumentFormatDetector();
        var dispatcher = new DocumentExtractionDispatcher(extractors, detector, NullLogger<DocumentExtractionDispatcher>.Instance);

        var input = DocumentInput.FromFile("file.xyz");
        await Assert.ThrowsAsync<NotSupportedException>(() => dispatcher.ExtractAsync(input));
    }

    [Fact]
    public void TextNormalizer_RemovesControlChars()
    {
        string input = "Hello\x00World\n\nLine2";
        string result = TextNormalizer.NormalizeAll(input);
        Assert.DoesNotContain('\x00', result);
        Assert.Contains("Hello", result);
        Assert.Contains("Line2", result);
    }

    [Fact]
    public void TextNormalizer_NormalizesLineEndings()
    {
        string input = "Line1\r\nLine2\rLine3\nLine4";
        string result = TextNormalizer.Normalize(input);
        Assert.DoesNotContain('\r', result);
        Assert.Contains("Line1\nLine2", result);
    }

    [Fact]
    public void TextNormalizer_CollapsesBlankLines()
    {
        string input = "A\n\n\n\nB";
        string result = TextNormalizer.CollapseBlankLines(input);
        Assert.DoesNotContain("\n\n\n", result);
    }

    [Fact]
    public async Task Html_ExtractsHeadings()
    {
        string? path = GetSamplePath("sample.html");
        if (path == null) return;

        var extractor = new HtmlExtractor();
        var input = DocumentInput.FromFile(path);
        ExtractedDocument result = await extractor.ExtractAsync(input);

        var headings = result.Blocks.Where(b => b.Type == BlockType.Heading).ToList();
        Assert.NotEmpty(headings);
    }

    [Fact]
    public async Task Docx_BlocksHaveNonEmptyText()
    {
        string? path = GetSamplePath("sample.docx");
        if (path == null) return;

        var extractor = new DocxExtractor();
        var input = DocumentInput.FromFile(path);
        ExtractedDocument result = await extractor.ExtractAsync(input);

        Assert.All(result.Blocks, b => Assert.False(string.IsNullOrWhiteSpace(b.Text)));
    }

    [Fact]
    public async Task Markdown_ExtractsFromStream()
    {
        const string md = "# Hello World\n\nThis is a paragraph.\n\n## Section 2\n\nMore content.";
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(md));
        var input = DocumentInput.FromStream(ms, ".md", "test.md");

        var extractor = new MarkdownExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(input);

        Assert.NotEmpty(result.Blocks);
        Assert.Equal("Hello World", result.Title);
        var headings = result.Blocks.Where(b => b.Type == BlockType.Heading).ToList();
        Assert.NotEmpty(headings);
        Assert.Contains(headings, h => h.HeadingLevel == 1);
    }

    [Fact]
    public async Task PlainText_ExtractsFromStream()
    {
        const string txt = "First paragraph.\n\nSecond paragraph.\n\nThird paragraph.";
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(txt));
        var input = DocumentInput.FromStream(ms, ".txt", "inline.txt");

        var extractor = new PlainTextExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(input);

        Assert.True(result.Blocks.Count >= 2);
    }

    [Fact]
    public async Task Html_ExtractsFromStream()
    {
        const string html = "<html><head><title>My Page</title></head><body><h1>Heading</h1><p>Paragraph content.</p></body></html>";
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(html));
        var input = DocumentInput.FromStream(ms, ".html", "test.html");

        var extractor = new HtmlExtractor();
        ExtractedDocument result = await extractor.ExtractAsync(input);

        Assert.Equal("My Page", result.Title);
        Assert.NotEmpty(result.Blocks);
    }

    [Fact]
    public async Task StructureAwareChunker_EmptyBlocksReturnsEmpty()
    {
        StructureAwareChunker chunker = CreateChunker();
        IReadOnlyList<SearchChunk> chunks = chunker.Chunk("doc-id", "Title", "uri", ".txt", null, null, []);
        Assert.Empty(chunks);
    }
}
