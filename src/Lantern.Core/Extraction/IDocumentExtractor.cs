namespace Lantern.Core.Extraction;

public interface IDocumentExtractor
{
    bool CanExtract(DocumentFormat format);
    Task<ExtractedDocument> ExtractAsync(DocumentInput input, CancellationToken cancellationToken = default);
}
