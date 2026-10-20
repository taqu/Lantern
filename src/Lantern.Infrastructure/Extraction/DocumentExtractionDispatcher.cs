using Lantern.Core.Extraction;
using Microsoft.Extensions.Logging;

namespace Lantern.Infrastructure.Extraction;

public sealed class DocumentExtractionDispatcher
{
    private readonly IReadOnlyList<IDocumentExtractor> _extractors;
    private readonly DocumentFormatDetector _detector;
    private readonly ILogger<DocumentExtractionDispatcher> _logger;

    public DocumentExtractionDispatcher(
        IEnumerable<IDocumentExtractor> extractors,
        DocumentFormatDetector detector,
        ILogger<DocumentExtractionDispatcher> logger)
    {
        _extractors = extractors.ToList();
        _detector = detector;
        _logger = logger;
    }

    public DocFormat DetectFormat(DocumentInput input) => _detector.Detect(input);

    public async Task<ExtractedDocument> ExtractAsync(DocumentInput input, CancellationToken cancellationToken = default)
    {
        var format = _detector.Detect(input);
        if (format == DocFormat.Unknown || !DocumentFormatDetector.IsSupported(format))
            throw new NotSupportedException($"Unsupported format: {input.Extension}");

        var extractor = _extractors.FirstOrDefault(e => e.CanExtract(format));
        if (extractor == null)
            throw new NotSupportedException($"No extractor registered for format: {format}");

        _logger.LogDebug("Extracting {Format} from {FileName}", format, input.FileName ?? input.FilePath);
        return await extractor.ExtractAsync(input, cancellationToken);
    }
}
