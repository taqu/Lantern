using Lantern.Core.Search;
using Meilisearch;
using Microsoft.Extensions.Logging;

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

    public MeilisearchSearchService(
        MeilisearchOptions options,
        string? apiKey,
        ILogger<MeilisearchSearchService> logger)
    {
        _client = new MeilisearchClient(options.Url, apiKey ?? "");
        _indexName = options.IndexName;
        _logger = logger;
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
                Url: h.Url,
                Path: h.SourcePath,
                Snippet: BuildSnippet(h.Content),
                DocumentType: h.DocumentType
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
    }

    // -----------------------------------------------------------------------
    // Phase 0: legacy methods
    // -----------------------------------------------------------------------

    public async Task<SearchResult> SearchAsync(
        string indexUid,
        string query,
        CancellationToken cancellationToken = default)
    {
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
            }).ToList()
        };
    }

    public async Task IndexDocumentAsync(
        string indexUid,
        SearchDocument doc,
        CancellationToken cancellationToken = default)
    {
        var index = _client.Index(indexUid);
        var meili = new MeiliDocument
        {
            Id = doc.Id,
            Title = doc.Title,
            Content = doc.Content,
            SourcePath = doc.SourcePath,
            Url = doc.Url,
            DocumentType = doc.DocumentType,
        };
        var task = await index.AddDocumentsAsync(new[] { meili }, cancellationToken: cancellationToken);
        await _client.WaitForTaskAsync(task.TaskUid, timeoutMs: TaskTimeoutMs);
    }

    public async Task DeleteIndexAsync(string indexUid, CancellationToken cancellationToken = default)
    {
        var task = await _client.DeleteIndexAsync(indexUid, cancellationToken);
        await _client.WaitForTaskAsync(task.TaskUid, timeoutMs: TaskTimeoutMs);
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
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string? SourcePath { get; set; }
    public string? Url { get; set; }
    public string? DocumentType { get; set; }
}
