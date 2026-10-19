using Lantern.Core.Search;
using Meilisearch;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lantern.Infrastructure.Search;

/// <summary>
/// ISearchService implementation backed by a Meilisearch instance.
/// </summary>
public sealed class MeilisearchSearchService : ISearchService
{
    private const int PageSize = 10;
    private const double TaskTimeoutMs = 30_000;

    private readonly MeilisearchClient _client;
    private readonly string _indexName;
    private readonly ILogger<MeilisearchSearchService> _logger;
    private readonly IDocumentIndexingService _indexing;
    private readonly HybridSearchOptions _hybridOptions;

    public MeilisearchSearchService(
        MeilisearchOptions options,
        string? apiKey,
        ILogger<MeilisearchSearchService> logger,
        IDocumentIndexingService? indexing = null,
        HybridSearchOptions? hybridOptions = null)
    {
        _client = new MeilisearchClient(options.Url, apiKey ?? "");
        _indexName = options.IndexName;
        _logger = logger;
        _indexing = indexing ?? new MeilisearchIndexingService(_client, options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MeilisearchIndexingService>.Instance);
        _hybridOptions = hybridOptions ?? new HybridSearchOptions();
    }

    // -----------------------------------------------------------------------
    // Phase 1: paginated UI search
    // -----------------------------------------------------------------------

    public async Task<SearchPageResponse> SearchPageAsync(
        string query,
        int page,
        CancellationToken cancellationToken = default)
    {
        if (!SearchQueryValidator.IsValid(query))
            return EmptyResponse(query, page);

        page = SearchQueryValidator.NormalizePage(page);
        int offset = SearchQueryValidator.CalculateOffset(page, PageSize);

        try
        {
            await _indexing.EnsureIndexAsync(cancellationToken);
            var index = _client.Index(_indexName);
            var searchQuery = new SearchQuery
            {
                Limit = PageSize,
                Offset = offset,
            };

            var searchable = await index.SearchAsync<MeiliDocument>(query, searchQuery, cancellationToken);
            var results = (Meilisearch.SearchResult<MeiliDocument>)searchable;

            var items = results.Hits.Select(h => new SearchPageItem(
                Id: h.Id,
                Title: string.IsNullOrEmpty(h.Title) ? "(Untitled)" : h.Title,
                Url: h.Uri ?? h.Url,
                Path: h.Path ?? h.SourcePath,
                Snippet: BuildSnippet(h.Content),
                DocumentType: h.Extension?.TrimStart('.').ToUpperInvariant() ?? h.DocumentType ?? h.SourceType?.ToUpperInvariant(),
                SourceId: h.SourceId, SourceType: h.SourceType, Extension: h.Extension,
                MimeType: h.MimeType, Size: h.Size, ModifiedAt: h.ModifiedAt, IndexedAt: h.IndexedAt
            )).ToList();

            long totalHits = results.EstimatedTotalHits;

            return new SearchPageResponse(
                Items: items,
                TotalHits: totalHits,
                Page: page,
                PageSize: PageSize,
                Query: query);
        }
        catch (MeilisearchApiError ex)
        {
            _logger.LogError(ex, "Meilisearch API error during SearchPageAsync for query '{Query}'", query);
            throw new SearchException("Meilisearch returned an error during search.", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error contacting Meilisearch during SearchPageAsync for query '{Query}'", query);
            throw new SearchException("Could not reach the Meilisearch server.", ex);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            _logger.LogError(ex, "Meilisearch index initialization did not complete");
            throw new SearchException("The search index could not be initialized. Please try again later.", ex);
        }
    }

    // -----------------------------------------------------------------------
    // Phase 3.5: hybrid search over chunk index
    // -----------------------------------------------------------------------

    public async Task<SearchPageResponse> SearchChunksPageAsync(
        string query,
        int page,
        SearchMode mode,
        CancellationToken cancellationToken = default)
    {
        if (!SearchQueryValidator.IsValid(query))
            return EmptyResponse(query, page);

        page = SearchQueryValidator.NormalizePage(page);
        int offset = SearchQueryValidator.CalculateOffset(page, PageSize);

        try
        {
            var index = _client.Index(_hybridOptions.ChunkIndexName);
            var searchQuery = new SearchQuery
            {
                Limit = PageSize,
                Offset = offset,
            };

            if (mode == SearchMode.Hybrid && _hybridOptions.Enabled)
            {
                searchQuery.Hybrid = new HybridSearch
                {
                    Embedder = _hybridOptions.EmbedderName,
                    SemanticRatio = _hybridOptions.SemanticRatio
                };
            }

            var searchable = await index.SearchAsync<MeiliChunkDocument>(query, searchQuery, cancellationToken);
            var results = (Meilisearch.SearchResult<MeiliChunkDocument>)searchable;

            var items = results.Hits.Select(h => new SearchPageItem(
                Id: h.Id,
                Title: string.IsNullOrEmpty(h.Title) ? "(Untitled)" : h.Title,
                Url: h.Uri ?? "",
                Path: null,
                Snippet: BuildSnippet(h.Content),
                DocumentType: h.Extension?.TrimStart('.').ToUpperInvariant(),
                SourceId: h.SourceId, SourceType: h.SourceType, Extension: h.Extension,
                MimeType: null, Size: null, ModifiedAt: null, IndexedAt: default
            )).ToList();

            long totalHits = results.EstimatedTotalHits;

            return new SearchPageResponse(
                Items: items,
                TotalHits: totalHits,
                Page: page,
                PageSize: PageSize,
                Query: query);
        }
        catch (MeilisearchApiError ex)
        {
            _logger.LogError(ex, "Meilisearch API error during SearchChunksPageAsync for query '{Query}'", query);
            throw new SearchException("Meilisearch returned an error during chunk search.", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error contacting Meilisearch during SearchChunksPageAsync for query '{Query}'", query);
            throw new SearchException("Could not reach the Meilisearch server.", ex);
        }
    }

    // -----------------------------------------------------------------------
    // Phase 0: legacy methods
    // -----------------------------------------------------------------------

    public async Task<SearchResult> SearchAsync(
        string indexUid,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (indexUid == _indexName) await _indexing.EnsureIndexAsync(cancellationToken);
        var index = _client.Index(indexUid);
        var searchable = await index.SearchAsync<MeiliDocument>(query, cancellationToken: cancellationToken);
        var results = (Meilisearch.SearchResult<MeiliDocument>)searchable;

        return new SearchResult
        {
            IndexUid = indexUid,
            Query = query,
            TotalHits = results.EstimatedTotalHits,
            ProcessingTimeMs = results.ProcessingTimeMs,
            Hits = results.Hits.Select(h => new SearchDocument
            {
                Id = h.Id,
                Title = h.Title,
                Content = h.Content,
                SourcePath = h.SourcePath,
                Url = h.Url,
                DocumentType = h.DocumentType,
                Uri = h.Uri ?? h.Url ?? "", Path = h.Path ?? h.SourcePath,
                SourceId = h.SourceId ?? "", SourceType = h.SourceType ?? "",
                Extension = h.Extension, MimeType = h.MimeType, Size = h.Size,
                ModifiedAt = h.ModifiedAt, IndexedAt = h.IndexedAt,
            }).ToList()
        };
    }

    public async Task IndexDocumentAsync(
        string indexUid,
        SearchDocument doc,
        CancellationToken cancellationToken = default)
    {
        if (indexUid != _indexName) throw new ArgumentException("Use the configured indexing service for document writes.", nameof(indexUid));
        await _indexing.UpsertAsync(doc, cancellationToken);
    }

    public async Task DeleteIndexAsync(string indexUid, CancellationToken cancellationToken = default)
    {
        var task = await _client.DeleteIndexAsync(indexUid, cancellationToken);
        var result = await _client.WaitForTaskAsync(task.TaskUid, timeoutMs: TaskTimeoutMs, cancellationToken: cancellationToken);
        if (result.Status != TaskInfoStatus.Succeeded) throw new InvalidOperationException($"Delete index task {task.TaskUid} failed: {result.Error}");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static SearchPageResponse EmptyResponse(string query, int page) =>
        new(Items: Array.Empty<SearchPageItem>(),
            TotalHits: 0,
            Page: SearchQueryValidator.NormalizePage(page),
            PageSize: PageSize,
            Query: query ?? "");

    private static string? BuildSnippet(string? content)
    {
        if (string.IsNullOrEmpty(content)) return null;
        return content.Length <= 200 ? content : content[..200];
    }
}

/// <summary>
/// Internal document type used for Meilisearch serialization/deserialization.
/// </summary>
internal sealed class MeiliDocument
{
    public string? Uri { get; set; }
    public string? Path { get; set; }
    public string? SourceId { get; set; }
    public string? SourceType { get; set; }
    public string? Extension { get; set; }
    public string? MimeType { get; set; }
    public long? Size { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public DateTimeOffset IndexedAt { get; set; }
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string? SourcePath { get; set; }
    public string? Url { get; set; }
    public string? DocumentType { get; set; }
}

/// <summary>
/// Internal document type for chunk index deserialization.
/// </summary>
internal sealed class MeiliChunkDocument
{
    public string Id { get; set; } = "";
    public string? DocumentId { get; set; }
    public string? SourceId { get; set; }
    public string? SourceType { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public int ChunkIndex { get; set; }
    public string? SectionTitle { get; set; }
    public string? Uri { get; set; }
    public string? Extension { get; set; }
}
