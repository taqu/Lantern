using Lantern.Core.Extraction;
using Lantern.Core.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lantern.Infrastructure.Extraction;

public sealed class DocumentProcessingService : IDocumentProcessingService
{
    private readonly DocumentExtractionDispatcher _dispatcher;
    private readonly StructureAwareChunker _chunker;
    private readonly ExtractionOptions _options;
    private readonly ILogger<DocumentProcessingService> _logger;
    private readonly SemaphoreSlim _concurrencyLimiter;

    public DocumentProcessingService(
        DocumentExtractionDispatcher dispatcher,
        StructureAwareChunker chunker,
        IOptions<ExtractionOptions> options,
        ILogger<DocumentProcessingService> logger)
    {
        _dispatcher = dispatcher;
        _chunker = chunker;
        _options = options.Value;
        _logger = logger;
        _concurrencyLimiter = new SemaphoreSlim(_options.MaxConcurrentExtractions, _options.MaxConcurrentExtractions);
    }

    public async Task<DocumentProcessingResult> ProcessAsync(
        DocumentInput input,
        string documentId,
        string title,
        string uri,
        string? sourceId,
        string? sourceType,
        CancellationToken cancellationToken = default)
    {
        // Validate file size if we have a path
        if (input.FilePath != null && File.Exists(input.FilePath))
        {
            var fileInfo = new FileInfo(input.FilePath);
            long maxBytes = (long)_options.MaxFileSizeMB * 1024 * 1024;
            if (fileInfo.Length > maxBytes)
            {
                _logger.LogWarning("File too large: {Path} ({Size}MB > {Max}MB)",
                    input.FilePath, fileInfo.Length / 1024 / 1024, _options.MaxFileSizeMB);
                return Failure(ExtractionFailureCategory.FileTooLarge,
                    $"File exceeds the {_options.MaxFileSizeMB}MB limit");
            }
        }

        await _concurrencyLimiter.WaitAsync(cancellationToken);
        try
        {
            ExtractedDocument extracted;
            try
            {
                extracted = await _dispatcher.ExtractAsync(input, cancellationToken);
            }
            catch (NotSupportedException ex)
            {
                return Failure(ExtractionFailureCategory.UnsupportedFormat, ex.Message);
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("PDF_ENCRYPTED"))
            {
                return Failure(ExtractionFailureCategory.EncryptedDocument, "Document is encrypted or password-protected");
            }
            catch (OperationCanceledException)
            {
                return Failure(ExtractionFailureCategory.ExtractionTimeout, "Extraction was cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Extraction failed for {FileName}", input.FileName ?? input.FilePath);
                return Failure(ExtractionFailureCategory.ExtractionFailed, $"Extraction failed: {ex.Message}");
            }

            // Check extracted character limit
            int totalChars = extracted.Blocks.Sum(b => b.Text.Length);
            if (totalChars > _options.MaxExtractedCharacters)
            {
                _logger.LogWarning("Extracted text too large: {Chars} chars from {File}",
                    totalChars, input.FileName ?? input.FilePath);
                return Failure(ExtractionFailureCategory.ResourceLimitExceeded,
                    $"Extracted text ({totalChars} chars) exceeds limit of {_options.MaxExtractedCharacters}");
            }

            if (extracted.Blocks.Count == 0 || totalChars == 0)
            {
                return Failure(ExtractionFailureCategory.NoExtractableText,
                    "No extractable text found in document");
            }

            IReadOnlyList<SearchChunk> chunks = _chunker.Chunk(
                documentId, title, uri, input.Extension, sourceId, sourceType, extracted.Blocks);

            _logger.LogInformation("Processed {FileName}: {Blocks} blocks -> {Chunks} chunks",
                input.FileName ?? input.FilePath, extracted.Blocks.Count, chunks.Count);

            return new DocumentProcessingResult
            {
                Success = true,
                Document = extracted,
                Chunks = chunks
            };
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    private static DocumentProcessingResult Failure(ExtractionFailureCategory category, string message) =>
        new() { Success = false, FailureCategory = category, FailureMessage = message };
}
