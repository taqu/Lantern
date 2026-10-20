using System.Text;
using Lantern.Core.Extraction;
using Lantern.Core.Search;
using Lantern.Infrastructure.Extraction;
using Lantern.Infrastructure.Search;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lantern.IntegrationTests.Extraction;

/// <summary>
/// Phase 4.5 tests: general text extraction, encoding detection, binary classification,
/// source code preservation, and line number tracking.
/// All tests run without Meilisearch or embedding server.
/// </summary>
public sealed class GeneralTextExtractionTests
{
    private readonly GeneralTextExtractor _extractor = new();
    private readonly EncodingDetector _detector = new();

    // ── Encoding detection ────────────────────────────────────────────────────

    [Fact]
    public void Detect_Utf8WithBom_ReturnsCorrectEncoding()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("hello")];
        EncodingDetector.DetectionResult result = _detector.Detect(bytes);
        Assert.Equal("utf-8-bom", result.EncodingName);
        Assert.Equal(3, result.BomLength);
        Assert.True(result.IsReliable);
        Assert.Equal("hello", _detector.Decode(bytes, result));
    }

    [Fact]
    public void Detect_Utf8WithoutBom_ReturnsUtf8()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("hello world");
        EncodingDetector.DetectionResult result = _detector.Detect(bytes);
        Assert.Equal("utf-8", result.EncodingName);
        Assert.Equal(0, result.BomLength);
        Assert.True(result.IsReliable);
        Assert.Equal("hello world", _detector.Decode(bytes, result));
    }

    [Fact]
    public void Detect_Utf16Le_ReturnsBomEncoding()
    {
        byte[] bytes = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("hello")];
        EncodingDetector.DetectionResult result = _detector.Detect(bytes);
        Assert.Equal("utf-16-le", result.EncodingName);
        Assert.Equal(2, result.BomLength);
        Assert.Equal("hello", _detector.Decode(bytes, result));
    }

    [Fact]
    public void Detect_Utf16Be_ReturnsBomEncoding()
    {
        byte[] bytes = [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes("hello")];
        EncodingDetector.DetectionResult result = _detector.Detect(bytes);
        Assert.Equal("utf-16-be", result.EncodingName);
        Assert.Equal(2, result.BomLength);
        Assert.Equal("hello", _detector.Decode(bytes, result));
    }

    [Fact]
    public void Detect_Utf8JapaneseText_DecodesCorrectly()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("日本語テスト");
        EncodingDetector.DetectionResult result = _detector.Detect(bytes);
        Assert.Equal("utf-8", result.EncodingName);
        Assert.Equal("日本語テスト", _detector.Decode(bytes, result));
    }

    [Fact]
    public void Detect_ShiftJis_JapaneseText_DoesNotThrow()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var shiftJis = Encoding.GetEncoding(932);
        byte[] bytes = shiftJis.GetBytes("日本語テスト"); // invalid UTF-8
        EncodingDetector.DetectionResult result = _detector.Detect(bytes);
        // Must not throw; result has an encoding name
        Assert.NotNull(result.EncodingName);
        string decoded = _detector.Decode(bytes, result);
        Assert.NotNull(decoded);
        Assert.NotEmpty(decoded);
    }

    [Fact]
    public void Detect_InvalidUtf8_FallsBackGracefully()
    {
        // Lone continuation byte followed by valid ASCII
        byte[] bytes = [0xC3, 0x28, 0x41, 0x42, 0x43];
        EncodingDetector.DetectionResult result = _detector.Detect(bytes);
        Assert.NotNull(result.EncodingName);
        Assert.NotNull(result.Encoding);
        // Must not throw
    }

    [Fact]
    public void Detect_ExplicitHint_UsesHint()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("test");
        EncodingDetector.DetectionResult result = _detector.Detect(bytes, hintEncoding: "utf-8");
        Assert.Contains("utf", result.EncodingName, StringComparison.OrdinalIgnoreCase);
    }

    // ── Binary classification ─────────────────────────────────────────────────

    [Fact]
    public void Classify_PlainAscii_IsText()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("Hello world\nLine 2\n");
        Assert.Equal(TextBinaryClassifier.Classification.Text, TextBinaryClassifier.ClassifyBytes(bytes));
    }

    [Fact]
    public void Classify_Utf8Japanese_IsText()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("日本語テスト\ntest\n");
        Assert.Equal(TextBinaryClassifier.Classification.Text, TextBinaryClassifier.ClassifyBytes(bytes));
    }

    [Fact]
    public void Classify_Utf16LeBom_IsText()
    {
        byte[] bytes = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("hello")];
        Assert.Equal(TextBinaryClassifier.Classification.Text, TextBinaryClassifier.ClassifyBytes(bytes));
    }

    [Fact]
    public void Classify_NullBytes_IsBinary()
    {
        byte[] bytes = [0x48, 0x65, 0x6C, 0x00, 0x6C, 0x6F]; // "Hel\0lo"
        Assert.Equal(TextBinaryClassifier.Classification.Binary, TextBinaryClassifier.ClassifyBytes(bytes));
    }

    [Fact]
    public void Classify_HighNonPrintable_IsBinary()
    {
        byte[] bytes = new byte[100];
        for (int i = 0; i < 40; i++) bytes[i] = 0x01; // SOH control char
        for (int i = 40; i < 100; i++) bytes[i] = 0x41; // 'A'
        Assert.Equal(TextBinaryClassifier.Classification.Binary, TextBinaryClassifier.ClassifyBytes(bytes));
    }

    [Fact]
    public void Classify_Empty_IsText()
    {
        Assert.Equal(TextBinaryClassifier.Classification.Text, TextBinaryClassifier.ClassifyBytes([]));
    }

    // ── GeneralTextExtractor: source code ─────────────────────────────────────

    [Fact]
    public async Task Extract_CSharp_PreservesIndentation()
    {
        const string code = "namespace MyApp;\n\npublic class Foo\n{\n    private int _value;\n\n    public void Bar()\n    {\n        _value = 42;\n    }\n}\n";
        ExtractedDocument result = await _extractor.ExtractAsync(StreamInput(code, ".cs"));

        string allText = string.Join("\n", result.Blocks.Select(b => b.Text));
        Assert.Contains("    private int _value;", allText);
        Assert.Contains("        _value = 42;", allText);
    }

    [Fact]
    public async Task Extract_Python_PreservesComments()
    {
        const string code = "# Top comment\ndef greet(name):\n    # Inner comment\n    return f\"Hello, {name}\"\n";
        ExtractedDocument result = await _extractor.ExtractAsync(StreamInput(code, ".py"));

        string allText = string.Join("\n", result.Blocks.Select(b => b.Text));
        Assert.Contains("# Top comment", allText);
        Assert.Contains("# Inner comment", allText);
    }

    [Fact]
    public async Task Extract_Cpp_PreservesPreprocessorDirectives()
    {
        const string code = "#include <stdio.h>\n#define MAX 100\n\nint main() {\n    printf(\"Hi\\n\");\n    return 0;\n}\n";
        ExtractedDocument result = await _extractor.ExtractAsync(StreamInput(code, ".cpp"));

        string allText = string.Join("\n", result.Blocks.Select(b => b.Text));
        Assert.Contains("#include <stdio.h>", allText);
        Assert.Contains("#define MAX 100", allText);
    }

    [Fact]
    public async Task Extract_Rust_PreservesCode()
    {
        const string code = "fn main() {\n    let x: u32 = 42;\n    println!(\"x = {}\", x);\n}\n";
        ExtractedDocument result = await _extractor.ExtractAsync(StreamInput(code, ".rs"));

        string allText = string.Join("\n", result.Blocks.Select(b => b.Text));
        Assert.Contains("let x: u32 = 42;", allText);
        Assert.Contains("println!", allText);
    }

    [Fact]
    public async Task Extract_Json_PreservesStructure()
    {
        const string json = "{\n  \"name\": \"Lantern\",\n  \"enabled\": true\n}\n";
        ExtractedDocument result = await _extractor.ExtractAsync(StreamInput(json, ".json"));

        string allText = string.Join("\n", result.Blocks.Select(b => b.Text));
        Assert.Contains("\"name\"", allText);
        Assert.Contains("\"Lantern\"", allText);
    }

    [Fact]
    public async Task Extract_Yaml_PreservesStructure()
    {
        const string yaml = "server:\n  host: localhost\n  port: 8080\n\ndatabase:\n  url: sqlite:///data.db\n";
        ExtractedDocument result = await _extractor.ExtractAsync(StreamInput(yaml, ".yaml"));

        string allText = string.Join("\n", result.Blocks.Select(b => b.Text));
        Assert.Contains("server:", allText);
        Assert.Contains("port: 8080", allText);
    }

    [Fact]
    public async Task Extract_Xml_PreservesContent()
    {
        const string xml = "<?xml version=\"1.0\"?>\n<root>\n  <item key=\"v\">content</item>\n</root>\n";
        ExtractedDocument result = await _extractor.ExtractAsync(StreamInput(xml, ".xml"));

        string allText = string.Join("\n", result.Blocks.Select(b => b.Text));
        Assert.Contains("content", allText);
        Assert.Contains("<item", allText);
    }

    [Fact]
    public async Task Extract_LineNumbers_AreSet()
    {
        const string code = "line1\nline2\nline3\n\n\nline6\nline7\n";
        ExtractedDocument result = await _extractor.ExtractAsync(StreamInput(code, ".cs"));

        Assert.All(result.Blocks, b =>
        {
            Assert.True(b.StartLine.HasValue, "StartLine should be set");
            Assert.True(b.EndLine.HasValue, "EndLine should be set");
            Assert.True(b.StartLine >= 1, "StartLine should be 1-based");
            Assert.True(b.EndLine >= b.StartLine);
        });
    }

    [Fact]
    public async Task Extract_LineNumbers_PropagatedToChunks()
    {
        const string code = "line1\nline2\n\n\nline5\nline6\n";
        ExtractedDocument extracted = await _extractor.ExtractAsync(StreamInput(code, ".py"));

        if (!extracted.Blocks.Any(b => b.StartLine.HasValue)) return; // skip if no line info

        IOptions<ChunkingOptions> opts = Options.Create(new ChunkingOptions());
        var textChunker = new TextChunkingService(opts);
        var chunker = new StructureAwareChunker(textChunker, opts);
        IReadOnlyList<SearchChunk> chunks = chunker.Chunk("doc-id", "Test", "file:///test.py", ".py", "src", "file", extracted.Blocks);

        Assert.True(chunks.Any(c => c.StartLine.HasValue), "At least one chunk should have StartLine");
    }

    [Fact]
    public async Task Extract_JapaneseCode_Preserved()
    {
        const string code = "// 日本語のコメント\npublic class 検索 {\n    private string _名前;\n}\n";
        ExtractedDocument result = await _extractor.ExtractAsync(StreamInput(code, ".cs"));

        string allText = string.Join("\n", result.Blocks.Select(b => b.Text));
        Assert.Contains("日本語のコメント", allText);
        Assert.Contains("検索", allText);
    }

    [Fact]
    public async Task Extract_Utf8Bom_DecodesCorrectly()
    {
        byte[] bom = [0xEF, 0xBB, 0xBF];
        byte[] content = Encoding.UTF8.GetBytes("hello world\nline 2\n");
        byte[] bytes = [.. bom, .. content];
        using var ms = new MemoryStream(bytes);
        ExtractedDocument result = await _extractor.ExtractAsync(DocumentInput.FromStream(ms, ".txt", "test.txt"));

        Assert.Equal("utf-8-bom", result.Metadata.DetectedEncoding);
        string allText = string.Join(" ", result.Blocks.Select(b => b.Text));
        Assert.Contains("hello world", allText);
    }

    [Fact]
    public async Task Extract_Utf16Le_DecodesCorrectly()
    {
        byte[] bytes = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("hello world\nline 2\n")];
        using var ms = new MemoryStream(bytes);
        ExtractedDocument result = await _extractor.ExtractAsync(DocumentInput.FromStream(ms, ".txt", "test.txt"));

        Assert.Equal("utf-16-le", result.Metadata.DetectedEncoding);
        string allText = string.Join(" ", result.Blocks.Select(b => b.Text));
        Assert.Contains("hello world", allText);
    }

    [Fact]
    public async Task Extract_Utf16Be_DecodesCorrectly()
    {
        byte[] bytes = [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes("hello world\nline 2\n")];
        using var ms = new MemoryStream(bytes);
        ExtractedDocument result = await _extractor.ExtractAsync(DocumentInput.FromStream(ms, ".txt", "test.txt"));

        Assert.Equal("utf-16-be", result.Metadata.DetectedEncoding);
        string allText = string.Join(" ", result.Blocks.Select(b => b.Text));
        Assert.Contains("hello world", allText);
    }

    [Fact]
    public async Task Extract_BinaryInput_ThrowsInvalidOperation()
    {
        byte[] bytes = [0x89, 0x50, 0x4E, 0x47, 0x00, 0x00, 0x00, 0x0D]; // PNG header (null bytes)
        using var ms = new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _extractor.ExtractAsync(DocumentInput.FromStream(ms, ".bin")));
    }

    [Fact]
    public async Task Extract_EmptyInput_ReturnsEmptyBlocks()
    {
        ExtractedDocument result = await _extractor.ExtractAsync(StreamInput("", ".cs"));
        Assert.Empty(result.Blocks);
    }

    [Fact]
    public async Task Extract_WhitespaceOnly_ReturnsEmptyBlocks()
    {
        ExtractedDocument result = await _extractor.ExtractAsync(StreamInput("   \n  \n\n  ", ".cs"));
        Assert.Empty(result.Blocks);
    }

    [Fact]
    public async Task Extract_OriginalBytesNotModified()
    {
        byte[] original = Encoding.UTF8.GetBytes("hello\nworld\n");
        byte[] copy = (byte[])original.Clone();
        using var ms = new MemoryStream(original);
        await _extractor.ExtractAsync(DocumentInput.FromStream(ms, ".txt"));
        Assert.Equal(copy, original);
    }

    [Fact]
    public async Task Extract_ExtensionlessContent_Succeeds()
    {
        const string content = "all:\n\t@echo hello\n\nclean:\n\t@rm -rf build\n";
        ExtractedDocument result = await _extractor.ExtractAsync(StreamInput(content, ""));
        Assert.NotEmpty(result.Blocks);
        string allText = string.Join("\n", result.Blocks.Select(b => b.Text));
        Assert.Contains("@echo hello", allText);
    }

    // ── DocumentFormatDetector: extension mapping ─────────────────────────────

    [Theory]
    [InlineData(".cs")]
    [InlineData(".py")]
    [InlineData(".rs")]
    [InlineData(".ts")]
    [InlineData(".cpp")]
    [InlineData(".java")]
    [InlineData(".go")]
    [InlineData(".rb")]
    [InlineData(".sql")]
    [InlineData(".json")]
    [InlineData(".yaml")]
    [InlineData(".yml")]
    [InlineData(".xml")]
    [InlineData(".sh")]
    [InlineData(".ps1")]
    [InlineData(".toml")]
    [InlineData(".ini")]
    [InlineData(".cfg")]
    [InlineData(".log")]
    public void Detector_SourceExtensions_MappedToGeneralText(string ext)
    {
        var detector = new DocumentFormatDetector();
        var input = DocumentInput.FromFile($"file{ext}");
        Assert.Equal(DocFormat.GeneralText, detector.Detect(input));
    }

    [Theory]
    [InlineData("Makefile")]
    [InlineData("Dockerfile")]
    [InlineData("LICENSE")]
    [InlineData(".gitignore")]
    [InlineData(".editorconfig")]
    public void Detector_KnownExtensionlessFilenames_MappedToGeneralText(string filename)
    {
        var detector = new DocumentFormatDetector();
        // FromFile with just the filename — no directory, gets FileName set
        var input = DocumentInput.FromFile(filename);
        Assert.Equal(DocFormat.GeneralText, detector.Detect(input));
    }

    [Fact]
    public void Detector_GeneralText_IsSupported()
    {
        Assert.True(DocumentFormatDetector.IsSupported(DocFormat.GeneralText));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DocumentInput StreamInput(string text, string extension)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        return DocumentInput.FromStream(new MemoryStream(bytes), extension, $"test{extension}");
    }
}
