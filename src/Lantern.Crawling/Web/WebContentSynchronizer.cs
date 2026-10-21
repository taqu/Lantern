using System.Security.Cryptography;
using System.Text;
using Lantern.Core.Administration;
using Lantern.Core.Crawling;
using Lantern.Core.Extraction;
using Lantern.Core.Search;
using Microsoft.Extensions.Logging;

namespace Lantern.Crawling.Web;

public enum WebSyncOutcome { Indexed, Updated, Unchanged, Failed, Skipped }

/// <summary>
/// Synchronizes a fetched URL's content into the search index.
/// Reuses the Phase 4 extraction pipeline and Phase 3.5 indexing infrastructure.
/// </summary>
public sealed class WebContentSynchronizer(
    IDocumentProcessingService processingService,
    IDocumentIndexingService documentIndexing,
    IChunkIndexingService chunkIndexing,
    ICrawledUrlStateRepository stateRepository,
    ICrawlerCategoryResolver categoryResolver,
    ILogger<WebContentSynchronizer> logger)
{
    private string[]? _categoryIds;
    private Guid _lastCrawlerIdForCategories = Guid.Empty;

    private async Task<string[]> GetCategoryIdsAsync(Guid crawlerId, CancellationToken ct)
    {
        if (_categoryIds != null && _lastCrawlerIdForCategories == crawlerId)
            return _categoryIds;
        _categoryIds = await categoryResolver.GetCategoryKeysAsync(crawlerId, ct);
        _lastCrawlerIdForCategories = crawlerId;
        return _categoryIds;
    }

    public async Task<(WebSyncOutcome Outcome, string? Error)> SyncAsync(
        string canonicalUrl,
        byte[] body,
        string? contentType,
        string? etag,
        string? lastModified,
        Guid crawlerId,
        Guid scanId,
        CancellationToken ct)
    {
        UrlState? existing = await stateRepository.FindAsync(crawlerId, canonicalUrl, ct);
        string contentHash = ComputeHash(body);

        // Unchanged check: same ETag or same content hash
        if (existing is { SyncStatus: UrlSyncStatus.Indexed }
            && (!string.IsNullOrEmpty(etag) && etag == existing.ETag
                || (string.IsNullOrEmpty(etag) && contentHash == existing.ContentHash)))
        {
            existing.LastSeenScanId = scanId;
            existing.LastFetchedAt = DateTimeOffset.UtcNow;
            existing.LastHttpStatus = 200;
            await stateRepository.SaveAsync(existing, ct);
            return (WebSyncOutcome.Unchanged, null);
        }

        bool isNew = existing == null;
        string extension = GetExtension(canonicalUrl, contentType);
        string documentId = DocumentIdentity.Generate("web", crawlerId.ToString(), canonicalUrl);
        string fileName = GetFileName(canonicalUrl);

        var input = DocumentInput.FromStream(new MemoryStream(body), extension, fileName, contentType);
        DocumentProcessingResult result;
        try
        {
            result = await processingService.ProcessAsync(
                input, documentId, fileName, canonicalUrl, crawlerId.ToString(), "web", ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Extraction failed for {Url}", canonicalUrl);
            await PersistFailureAsync(existing, canonicalUrl, crawlerId, documentId, scanId, ex.Message, etag, lastModified, contentHash, ct);
            return (WebSyncOutcome.Failed, ex.Message);
        }

        if (!result.Success)
        {
            await PersistFailureAsync(existing, canonicalUrl, crawlerId, documentId, scanId, result.FailureMessage, etag, lastModified, contentHash, ct);
            return (WebSyncOutcome.Failed, result.FailureMessage);
        }

        string extractedTitle = result.Document?.Metadata.Title ?? "";
        string title = !string.IsNullOrWhiteSpace(extractedTitle) ? extractedTitle : fileName;
        string content = BuildContent(result.Document);
        string[] categoryIds = await GetCategoryIdsAsync(crawlerId, ct);

        SearchDocument doc;
        try
        {
            doc = DocumentNormalizer.Normalize(new SearchDocument
            {
                SourceType = "web",
                SourceId = crawlerId.ToString(),
                Uri = canonicalUrl,
                Title = title,
                Content = content,
                Extension = string.IsNullOrEmpty(extension) ? null : extension,
                MimeType = contentType,
                Size = body.Length,
                IndexedAt = DateTimeOffset.UtcNow,
                CategoryIds = categoryIds
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Document normalization failed for {Url}", canonicalUrl);
            await PersistFailureAsync(existing, canonicalUrl, crawlerId, documentId, scanId, ex.Message, etag, lastModified, contentHash, ct);
            return (WebSyncOutcome.Failed, ex.Message);
        }

        IReadOnlyList<SearchChunk> chunks = categoryIds.Length > 0
            ? (result.Chunks ?? []).Select(c => new SearchChunk
            {
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

        try
        {
            await documentIndexing.UpsertAsync(doc, ct);
            await chunkIndexing.ReplaceDocumentChunksAsync(documentId, chunks, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Indexing failed for {Url}", canonicalUrl);
            await PersistFailureAsync(existing, canonicalUrl, crawlerId, documentId, scanId, ex.Message, etag, lastModified, contentHash, ct);
            return (WebSyncOutcome.Failed, ex.Message);
        }

        UrlState state = existing ?? new UrlState { CrawlerId = crawlerId, CanonicalUrl = canonicalUrl };
        state.DocumentId = documentId;
        state.ETag = etag;
        state.LastModified = lastModified;
        state.ContentHash = contentHash;
        state.LastSeenScanId = scanId;
        state.LastFetchedAt = DateTimeOffset.UtcNow;
        state.LastIndexedAt = DateTimeOffset.UtcNow;
        state.LastHttpStatus = 200;
        state.SyncStatus = UrlSyncStatus.Indexed;
        state.LastError = null;
        await stateRepository.SaveAsync(state, ct);
        return (isNew ? WebSyncOutcome.Indexed : WebSyncOutcome.Updated, null);
    }

    public async Task MarkNotModifiedAsync(string canonicalUrl, Guid crawlerId, Guid scanId, CancellationToken ct)
    {
        UrlState? state = await stateRepository.FindAsync(crawlerId, canonicalUrl, ct);
        if (state == null) return;
        state.LastSeenScanId = scanId;
        state.LastFetchedAt = DateTimeOffset.UtcNow;
        state.LastHttpStatus = 304;
        await stateRepository.SaveAsync(state, ct);
    }

    private async Task PersistFailureAsync(
        UrlState? existing, string canonicalUrl, Guid crawlerId, string documentId,
        Guid scanId, string? error, string? etag, string? lastModified, string? contentHash, CancellationToken ct)
    {
        UrlState state = existing ?? new UrlState { CrawlerId = crawlerId, CanonicalUrl = canonicalUrl };
        state.DocumentId ??= documentId;
        state.ETag = etag;
        state.LastModified = lastModified;
        state.ContentHash = contentHash;
        state.LastSeenScanId = scanId;
        state.LastFetchedAt = DateTimeOffset.UtcNow;
        state.LastHttpStatus = 200;
        state.SyncStatus = UrlSyncStatus.Failed;
        state.LastError = error;
        try { await stateRepository.SaveAsync(state, ct); }
        catch (Exception ex) { logger.LogError(ex, "Could not persist failure state for {Url}", canonicalUrl); }
    }

    private static string ComputeHash(byte[] data)
    {
        byte[] hash = SHA256.HashData(data);
        return Convert.ToHexStringLower(hash[..8]);
    }

    private static string BuildContent(Core.Extraction.ExtractedDocument? doc)
    {
        if (doc == null || doc.Blocks.Count == 0) return "";
        var sb = new StringBuilder();
        foreach (var block in doc.Blocks)
        {
            sb.Append(block.Text);
            if (sb.Length >= 4096) break;
            sb.Append(' ');
        }
        return sb.ToString().Trim();
    }

    private static string GetExtension(string url, string? contentType)
    {
        if (!string.IsNullOrEmpty(contentType))
        {
            string ct = contentType.Split(';')[0].Trim().ToLowerInvariant();
            return ct switch
            {
                "text/html" or "application/xhtml+xml" => ".html",
                "application/pdf" => ".pdf",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
                "application/vnd.openxmlformats-officedocument.presentationml.presentation" => ".pptx",
                "text/plain" => ".txt",
                "text/markdown" => ".md",
                _ => ""
            };
        }
        string path = Uri.TryCreate(url, UriKind.Absolute, out Uri? u) ? u.AbsolutePath : url;
        return Path.GetExtension(path).ToLowerInvariant();
    }

    private static string GetFileName(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? u)) return "page";
        string path = u.AbsolutePath.TrimEnd('/');
        string name = Path.GetFileName(path);
        return string.IsNullOrEmpty(name) ? u.Host : name;
    }
}
