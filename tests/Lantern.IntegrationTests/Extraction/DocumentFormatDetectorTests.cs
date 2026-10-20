using Lantern.Core.Extraction;
using Lantern.Infrastructure.Extraction;

namespace Lantern.IntegrationTests.Extraction;

public sealed class DocumentFormatDetectorTests
{
    private readonly DocumentFormatDetector _detector = new();

    [Theory]
    [InlineData(".txt", DocFormat.PlainText)]
    [InlineData(".md", DocFormat.Markdown)]
    [InlineData(".markdown", DocFormat.Markdown)]
    [InlineData(".html", DocFormat.Html)]
    [InlineData(".htm", DocFormat.Html)]
    [InlineData(".pdf", DocFormat.Pdf)]
    [InlineData(".docx", DocFormat.Docx)]
    [InlineData(".xlsx", DocFormat.Xlsx)]
    [InlineData(".pptx", DocFormat.Pptx)]
    [InlineData(".csv", DocFormat.Csv)]
    [InlineData(".xyz", DocFormat.Unknown)]
    public void Detect_ByExtension(string ext, DocFormat expected)
    {
        var input = DocumentInput.FromFile($"file{ext}");
        Assert.Equal(expected, _detector.Detect(input));
    }

    [Theory]
    [InlineData(".TXT", DocFormat.PlainText)]
    [InlineData(".PDF", DocFormat.Pdf)]
    [InlineData(".DOCX", DocFormat.Docx)]
    public void Detect_CaseInsensitive(string ext, DocFormat expected)
    {
        var input = DocumentInput.FromFile($"file{ext}");
        Assert.Equal(expected, _detector.Detect(input));
    }

    [Theory]
    [InlineData(DocFormat.PlainText, true)]
    [InlineData(DocFormat.Markdown, true)]
    [InlineData(DocFormat.Html, true)]
    [InlineData(DocFormat.Pdf, true)]
    [InlineData(DocFormat.Docx, true)]
    [InlineData(DocFormat.Xlsx, true)]
    [InlineData(DocFormat.Pptx, true)]
    [InlineData(DocFormat.Csv, true)]
    [InlineData(DocFormat.Unknown, false)]
    public void IsSupported_ReturnsCorrectValue(DocFormat format, bool expected)
    {
        Assert.Equal(expected, DocumentFormatDetector.IsSupported(format));
    }
}
