using System.Text;
using Lantern.Core.Administration;
using Lantern.Core.Crawling;
using Lantern.Core.Extraction;
using Lantern.Core.Search;
using Microsoft.Extensions.Logging;

namespace Lantern.Crawling.Files;

public sealed class FileSynchronizationService(
    IDocumentProcessingService processingService,
    IDocumentIndexingService documentIndexing,
    IChunkIndexingService chunkIndexing,
    ICrawlStateRepository stateRepository,
    ICrawlerCategoryResolver categoryResolver,
    ILogger<FileSynchronizationService> logger,
    HybridSearchReadiness? readiness = null)
{
    private string[]? _categoryIds;
    private Guid _lastCrawlerIdForCategories = Guid.Empty;

    private async Task<string[]> GetCategoryIdsAsync(Guid crawlerId, CancellationToken cancellationToken)
    {
        if(_categoryIds != null && _lastCrawlerIdForCategories == crawlerId)
            return _categoryIds;
        _categoryIds = await categoryResolver.GetCategoryKeysAsync(crawlerId, cancellationToken);
        _lastCrawlerIdForCategories = crawlerId;
        return _categoryIds;
    }

    public async Task<(FileSyncOutcome Outcome, string? Error)> SyncAsync(
        DiscoveredFile file,
        Guid crawlerId,
        Guid scanId,
        CancellationToken cancellationToken)
    {
        FileState? existing = await stateRepository.FindAsync(crawlerId, file.CanonicalPath, cancellationToken);

        // Unchanged check — skip files that are fully indexed, have a pending task, or are deferred
        // while the embedder is still not ready (recovery happens automatically when it becomes ready).
        if(existing != null
            && existing.FileSize == file.FileSize
            && existing.LastWriteTimeUtc == file.LastWriteTimeUtc) {
            bool skip = existing.SyncStatus is CrawlSyncStatus.Indexed or CrawlSyncStatus.IndexingPending
                || (existing.SyncStatus == CrawlSyncStatus.WaitingForEmbeddingReadiness && readiness?.IsReady != true);
            if(skip) {
                existing.LastSeenScanId = scanId;
                await stateRepository.SaveAsync(existing, cancellationToken);
                return (FileSyncOutcome.Unchanged, null);
            }
        }

        bool isNew = existing == null;
        string documentId = DocumentIdentity.Generate("file", crawlerId.ToString(), file.CanonicalPath);
        string fileName = Path.GetFileName(file.CanonicalPath);
        string uri = new Uri(file.CanonicalPath).AbsoluteUri;

        DocumentInput input = DocumentInput.FromFile(file.CanonicalPath);
        DocumentProcessingResult result;
        try {
            result = await processingService.ProcessAsync(
                input, documentId, fileName, uri, crawlerId.ToString(), "file", cancellationToken);
        } catch(Exception ex) {
            logger.LogError(ex, "Extraction threw for {Path}", file.CanonicalPath);
            await PersistFailureAsync(existing, file, crawlerId, documentId, scanId, ex.Message, cancellationToken);
            return (FileSyncOutcome.Failed, ex.Message);
        }

        if(!result.Success) {
            await PersistFailureAsync(existing, file, crawlerId, documentId, scanId, result.FailureMessage, cancellationToken);
            return (FileSyncOutcome.Failed, result.FailureMessage);
        }

        // Snapshot metadata before and after to detect mid-processing changes
        long sizeAfter;
        DateTimeOffset lwAfter;
        try {
            FileInfo info = new FileInfo(file.CanonicalPath);
            sizeAfter = info.Length;
            lwAfter = info.LastWriteTimeUtc;
        } catch {
            sizeAfter = file.FileSize;
            lwAfter = file.LastWriteTimeUtc;
        }

        string? extractedTitle = result.Document!.Metadata.Title;
        string title = !string.IsNullOrWhiteSpace(extractedTitle) ? extractedTitle : fileName;
        string content = BuildContent(result.Document);
        string[] categoryIds = await GetCategoryIdsAsync(crawlerId, cancellationToken);

        SearchDocument doc;
        try {
            doc = DocumentNormalizer.Normalize(new SearchDocument {
                SourceType = "file",
                SourceId = crawlerId.ToString(),
                Path = file.CanonicalPath,
                Title = title,
                Content = content,
                Extension = Path.GetExtension(file.CanonicalPath),
                Size = file.FileSize,
                ModifiedAt = file.LastWriteTimeUtc,
                IndexedAt = DateTimeOffset.UtcNow,
                CategoryIds = categoryIds
            });
        } catch(Exception ex) {
            logger.LogError(ex, "Document normalization failed for {Path}", file.CanonicalPath);
            await PersistFailureAsync(existing, file, crawlerId, documentId, scanId, ex.Message, cancellationToken);
            return (FileSyncOutcome.Failed, ex.Message);
        }

        IReadOnlyList<SearchChunk> chunks = categoryIds.Length > 0
            ? (result.Chunks ?? []).Select(c => new SearchChunk {
                Id = c.Id,
                DocumentId = c.DocumentId,
                SourceId = c.SourceId,
                SourceType = c.SourceType,
                Title = c.Title,
                Content = c.Content,
                ChunkIndex = c.ChunkIndex,
                SectionTitle = c.SectionTitle,
                PageNumber = c.PageNumber,
                SlideNumber = c.SlideNumber,
                SheetName = c.SheetName,
                StartOffset = c.StartOffset,
                EndOffset = c.EndOffset,
                Uri = c.Uri,
                Extension = c.Extension,
                StartLine = c.StartLine,
                EndLine = c.EndLine,
                CategoryIds = categoryIds
            }).ToList()
            : result.Chunks ?? [];

        int parentTaskUid;
        try {
            parentTaskUid = await documentIndexing.SubmitUpsertAsync(doc, cancellationToken);
        } catch(Exception ex) {
            logger.LogError(ex, "Parent document indexing submission failed for {Path}", file.CanonicalPath);
            await PersistFailureAsync(existing, file, crawlerId, documentId, scanId, ex.Message, cancellationToken);
            return (FileSyncOutcome.Failed, ex.Message);
        }

        // Defer chunk indexing if the REST embedder is not yet verified. The parent document
        // is already submitted so keyword search remains functional for this file.
        if(readiness?.IsReady == false) {
            FileState deferred = existing ?? new FileState { CrawlerId = crawlerId, CanonicalPath = file.CanonicalPath };
            deferred.DocumentId = documentId;
            deferred.FileSize = file.FileSize;
            deferred.LastWriteTimeUtc = file.LastWriteTimeUtc;
            deferred.LastSeenScanId = scanId;
            deferred.SyncStatus = CrawlSyncStatus.WaitingForEmbeddingReadiness;
            deferred.PendingMeilisearchTaskUid = parentTaskUid;
            deferred.LastError = null;
            await stateRepository.SaveAsync(deferred, cancellationToken);
            logger.LogInformation(
                "Chunk indexing deferred for {Path}: waiting for embedding readiness", file.CanonicalPath);
            return (FileSyncOutcome.Deferred, null);
        }

        int? chunkTaskUid;
        try {
            chunkTaskUid = await chunkIndexing.ReplaceDocumentChunksAsync(documentId, chunks, cancellationToken);
        } catch(Exception ex) {
            logger.LogError(ex, "Chunk indexing submission failed for {Path}", file.CanonicalPath);
            await PersistFailureAsync(existing, file, crawlerId, documentId, scanId, ex.Message, cancellationToken);
            return (FileSyncOutcome.Failed, ex.Message);
        }

        // Only record the fingerprint that was valid when processing started; if file changed mid-flight, mark for retry
        FileState state = existing ?? new FileState { CrawlerId = crawlerId, CanonicalPath = file.CanonicalPath };
        state.DocumentId = documentId;
        state.FileSize = file.FileSize;
        state.LastWriteTimeUtc = file.LastWriteTimeUtc;
        state.LastSeenScanId = scanId;
        state.LastIndexedAt = DateTimeOffset.UtcNow;
        state.LastError = null;

        if(sizeAfter != file.FileSize || lwAfter != file.LastWriteTimeUtc) {
            // File changed while we were processing it; mark for retry next scan
            state.SyncStatus = CrawlSyncStatus.PendingRetry;
            state.PendingMeilisearchTaskUid = null;
            logger.LogWarning("File changed during processing: {Path}", file.CanonicalPath);
        } else {
            state.SyncStatus = CrawlSyncStatus.IndexingPending;
            // Prefer the chunk upsert task UID: that task calls the REST embedder and can fail
            // independently (e.g. Rejected URI). Fall back to parent task UID when hybrid search
            // is disabled or the document produced no chunks.
            state.PendingMeilisearchTaskUid = chunkTaskUid ?? parentTaskUid;
        }

        await stateRepository.SaveAsync(state, cancellationToken);
        return (isNew ? FileSyncOutcome.Indexed : FileSyncOutcome.Updated, null);
    }

    private async Task PersistFailureAsync(
        FileState? existing,
        DiscoveredFile file,
        Guid crawlerId,
        string documentId,
        Guid scanId,
        string? error,
        CancellationToken cancellationToken)
    {
        // Preserve existing indexed state – only update scan tracking and error fields
        FileState state = existing ?? new FileState {
            CrawlerId = crawlerId,
            DocumentId = documentId,
            CanonicalPath = file.CanonicalPath,
            FileSize = file.FileSize,
            LastWriteTimeUtc = file.LastWriteTimeUtc
        };
        state.LastSeenScanId = scanId;
        state.SyncStatus = CrawlSyncStatus.Failed;
        state.LastError = error;
        try { await stateRepository.SaveAsync(state, cancellationToken); } catch(Exception ex) { logger.LogError(ex, "Could not persist failure state for {Path}", file.CanonicalPath); }
    }

    private static string BuildContent(Core.Extraction.ExtractedDocument doc)
    {
        if(doc.Blocks.Count == 0) return "";
        StringBuilder builder = new System.Text.StringBuilder();
        foreach(ExtractedBlock block in doc.Blocks) {
            builder.Append(block.Text);
            if(builder.Length >= 4096) break;
            builder.Append(' ');
        }
        return builder.ToString().Trim();
    }
}
