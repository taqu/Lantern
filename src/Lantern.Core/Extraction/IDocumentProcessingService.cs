namespace Lantern.Core.Extraction;

public interface IDocumentProcessingService
{
    Task<DocumentProcessingResult> ProcessAsync(
        DocumentInput input,
        string documentId,
        string title,
        string uri,
        string? sourceId,
        string? sourceType,
        CancellationToken cancellationToken = default);
}
