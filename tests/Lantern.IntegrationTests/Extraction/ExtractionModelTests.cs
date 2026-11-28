using Lantern.Core.Extraction;

namespace Lantern.IntegrationTests.Extraction;

public sealed class ExtractionModelTests
{
    [Fact]
    public void ExtractedBlock_CreatesWithRequiredFields()
    {
        ExtractedBlock block = new ExtractedBlock(BlockType.Paragraph, "Hello world", PageNumber: 1);
        Assert.Equal(BlockType.Paragraph, block.Type);
        Assert.Equal("Hello world", block.Text);
        Assert.Equal(1, block.PageNumber);
    }

    [Fact]
    public void DocumentInput_FromFile_SetsExtension()
    {
        DocumentInput input = DocumentInput.FromFile("C:/docs/test.pdf");
        Assert.Equal(".pdf", input.Extension);
        Assert.Equal("test.pdf", input.FileName);
    }

    [Fact]
    public void DocumentInput_FromFile_ExtensionLowercased()
    {
        DocumentInput input = DocumentInput.FromFile("C:/docs/test.PDF");
        Assert.Equal(".pdf", input.Extension);
    }

    [Fact]
    public void DocumentProcessingResult_Failure_HasCategory()
    {
        DocumentProcessingResult result = new DocumentProcessingResult {
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
        using MemoryStream ms = new MemoryStream();
        DocumentInput input = DocumentInput.FromStream(ms, ".PDF", "file.pdf");
        Assert.Equal(".pdf", input.Extension);
        Assert.Equal("file.pdf", input.FileName);
    }

    [Fact]
    public void ExtractedDocumentMetadata_DefaultValues()
    {
        ExtractedDocumentMetadata meta = new ExtractedDocumentMetadata();
        Assert.Equal(DocFormat.Unknown, meta.Format);
        Assert.Null(meta.Title);
        Assert.Null(meta.Author);
        Assert.Null(meta.PageCount);
    }

    [Fact]
    public void ExtractedBlock_RecordEquality()
    {
        ExtractedBlock b1 = new ExtractedBlock(BlockType.Paragraph, "Hello");
        ExtractedBlock b2 = new ExtractedBlock(BlockType.Paragraph, "Hello");
        Assert.Equal(b1, b2);
    }
}
