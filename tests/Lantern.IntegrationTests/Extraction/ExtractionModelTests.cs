using Lantern.Core.Extraction;
using Lantern.Core.Search;

namespace Lantern.IntegrationTests.Extraction;

public sealed class ExtractionModelTests
{
    [Fact]
    public void ExtractedBlock_CreatesWithRequiredFields()
    {
        var block = new ExtractedBlock(BlockType.Paragraph, "Hello world", PageNumber: 1);
        Assert.Equal(BlockType.Paragraph, block.Type);
        Assert.Equal("Hello world", block.Text);
        Assert.Equal(1, block.PageNumber);
    }

    [Fact]
    public void DocumentInput_FromFile_SetsExtension()
    {
        var input = DocumentInput.FromFile("C:/docs/test.pdf");
        Assert.Equal(".pdf", input.Extension);
        Assert.Equal("test.pdf", input.FileName);
    }

    [Fact]
    public void DocumentInput_FromFile_ExtensionLowercased()
    {
        var input = DocumentInput.FromFile("C:/docs/test.PDF");
        Assert.Equal(".pdf", input.Extension);
    }

    [Fact]
    public void DocumentProcessingResult_Failure_HasCategory()
    {
        var result = new DocumentProcessingResult
        {
            Success = false,
            FailureCategory = ExtractionFailureCategory.FileTooLarge,
            FailureMessage = "File too big"
        };
        Assert.False(result.Success);
        Assert.Equal(ExtractionFailureCategory.FileTooLarge, result.FailureCategory);
    }

    [Fact]
    public void DocumentInput_FromStream_SetsExtension()
    {
        using var ms = new MemoryStream();
        var input = DocumentInput.FromStream(ms, ".PDF", "file.pdf");
        Assert.Equal(".pdf", input.Extension);
        Assert.Equal("file.pdf", input.FileName);
    }

    [Fact]
    public void ExtractedDocumentMetadata_DefaultValues()
    {
        var meta = new ExtractedDocumentMetadata();
        Assert.Equal(DocFormat.Unknown, meta.Format);
        Assert.Null(meta.Title);
        Assert.Null(meta.Author);
        Assert.Null(meta.PageCount);
    }

    [Fact]
    public void ExtractedBlock_RecordEquality()
    {
        var b1 = new ExtractedBlock(BlockType.Paragraph, "Hello");
        var b2 = new ExtractedBlock(BlockType.Paragraph, "Hello");
        Assert.Equal(b1, b2);
    }
}
