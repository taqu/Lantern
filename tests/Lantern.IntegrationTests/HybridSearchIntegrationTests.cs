using Lantern.Core.Search;
using Lantern.Infrastructure.Search;
using Meilisearch;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lantern.IntegrationTests;

/// <summary>
/// Integration tests for hybrid (semantic + keyword) search via MeilisearchSearchService.
///
/// Required environment variables:
///   MEILI_MASTER_KEY   — Meilisearch master key; tests are skipped when absent.
///   MEILI_HOST         — Meilisearch base URL (default: http://localhost:7700).
///
/// Optional:
///   MEILI_EMBED_URL    — REST embedder URL (e.g. http://127.0.0.1:7703/embed).
///                        When set, tests that require a live embedder are executed.
///                        When absent those tests are skipped.
/// </summary>
public sealed class HybridSearchIntegrationTests : IAsyncLifetime
{
    private const double TimeoutMs = 30_000;

    private readonly string? _masterKey;
    private readonly string _host;
    private readonly string? _embedUrl;

    private readonly string _docIndex;
    private readonly string _chunkIndex;

    private MeilisearchClient? _rawClient;
    private HybridSearchOptions _hybridOptions = null!;
    private MeilisearchOptions _meiliOptions = null!;
    private MeilisearchSearchService _service = null!;

    public HybridSearchIntegrationTests()
    {
        _masterKey = Environment.GetEnvironmentVariable("MEILI_MASTER_KEY");
        _host = Environment.GetEnvironmentVariable("MEILI_HOST") ?? "http://localhost:7700";
        _embedUrl = Environment.GetEnvironmentVariable("MEILI_EMBED_URL");

        string suffix = $"{Guid.NewGuid():N}";
        _docIndex = $"hybrid-test-docs-{suffix}";
        _chunkIndex = $"hybrid-test-chunks-{suffix}";
    }

    public Task InitializeAsync()
    {
        if(_masterKey == null) return Task.CompletedTask;

        _rawClient = new MeilisearchClient(_host, _masterKey);
        _hybridOptions = new HybridSearchOptions {
            Enabled = true,
            EmbedderName = "test-embedder",
            ChunkIndexName = _chunkIndex,
            SemanticRatio = 0.5
        };
        _meiliOptions = new MeilisearchOptions {
            Url = _host,
            IndexName = _docIndex,
            TaskTimeoutSeconds = 60
        };
        _service = new MeilisearchSearchService(
            new TestMeilisearchClientFactory(_rawClient),
            _meiliOptions,
            NullLogger<MeilisearchSearchService>.Instance,
            hybridOptions: _hybridOptions);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if(_rawClient == null) return;
        foreach(string uid in new[] { _docIndex, _chunkIndex }) {
            try {
                TaskInfo del = await _rawClient.DeleteIndexAsync(uid);
                await _rawClient.WaitForTaskAsync(del.TaskUid, timeoutMs: TimeoutMs);
            } catch { }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task EnsureChunkIndexWithDocumentsAsync(IEnumerable<SearchChunk> chunks)
    {
        // Drop any leftover index.
        try {
            TaskInfo del = await _rawClient!.DeleteIndexAsync(_chunkIndex);
            await _rawClient.WaitForTaskAsync(del.TaskUid, timeoutMs: TimeoutMs);
        } catch(MeilisearchApiError ex) when(ex.Code == "index_not_found") { }

        TaskInfo create = await _rawClient!.CreateIndexAsync(_chunkIndex, "id");
        await _rawClient.WaitForTaskAsync(create.TaskUid, timeoutMs: TimeoutMs);

        Meilisearch.Index index = _rawClient.Index(_chunkIndex);
        TaskInfo settings = await index.UpdateSettingsAsync(new Settings {
            SearchableAttributes = ["title", "sectionTitle", "content"],
            FilterableAttributes = ["documentId", "sourceId", "sourceType", "extension", "categoryIds"],
        });
        await _rawClient.WaitForTaskAsync(settings.TaskUid, timeoutMs: TimeoutMs);

        TaskInfo add = await index.AddDocumentsAsync(chunks.ToList(), primaryKey: "id");
        await _rawClient.WaitForTaskAsync(add.TaskUid, timeoutMs: TimeoutMs);
    }

    private async Task ConfigureEmbedderAsync()
    {
        var config = new EmbedderConfigurationService(new TestMeilisearchClientFactory(_rawClient!),
            Options.Create(_hybridOptions), Options.Create(_meiliOptions), NullLogger<EmbedderConfigurationService>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await config.ConfigureEmbedderAsync(_embedUrl!, timeout.Token);
        await config.VerifyConnectionAsync(timeout.Token);
    }

    private static SearchChunk MakeChunk(string docId, int idx, string title, string content, string sourceType = "file") =>
        new() {
            Id = ChunkIdentity.Generate(docId, idx),
            DocumentId = docId,
            SourceId = docId,
            SourceType = sourceType,
            Title = title,
            Content = content,
            ChunkIndex = idx,
            Uri = sourceType == "file" ? $"file:///C:/docs/{docId}.txt" : $"https://example.com/{docId}",
            Extension = sourceType == "file" ? ".txt" : null
        };

    // ── Chunk index identity ──────────────────────────────────────────────────

    [SkippableFact]
    public async Task SearchChunksPageAsync_UsesChunkIndex_NotDocumentIndex()
    {
        Skip.If(_masterKey == null, "MEILI_MASTER_KEY not set; skipping integration test.");

        string docId = "id-index-identity";
        await EnsureChunkIndexWithDocumentsAsync(new[] { MakeChunk(docId, 0, "Index identity", "unique token xyzzy123") });

        // The chunk index contains the doc; the document index does not exist.
        // A keyword search on the chunk index must find it.
        SearchPageResponse response = await _service.SearchChunksPageAsync("xyzzy123", 1, SearchMode.Keyword);

        Assert.NotNull(response);
        Assert.True(response.TotalHits > 0, "Expected at least one hit on the chunk index.");
        Assert.Contains(response.Items, i => i.Id == ChunkIdentity.Generate(docId, 0));
    }

    // ── Keyword search on chunk index ─────────────────────────────────────────

    [SkippableFact]
    public async Task SearchChunksPageAsync_KeywordMode_ReturnsPopulatedHits()
    {
        Skip.If(_masterKey == null, "MEILI_MASTER_KEY not set; skipping integration test.");

        string docId = "id-keyword-chunk";
        await EnsureChunkIndexWithDocumentsAsync(new[] {
            MakeChunk(docId, 0, "Keyword Search Test", "lightweight search engine"),
            MakeChunk("other-doc", 0, "Unrelated", "unrelated content here")
        });

        SearchPageResponse response = await _service.SearchChunksPageAsync("lightweight", 1, SearchMode.Keyword);

        Assert.NotNull(response);
        Assert.NotNull(response.Items);
        Assert.True(response.TotalHits > 0, "Expected hits for 'lightweight' in chunk content.");
        Assert.Contains(response.Items, i => i.Id == ChunkIdentity.Generate(docId, 0));
    }

    [SkippableFact]
    public async Task SearchChunksPageAsync_EmptyQuery_ReturnsEmptyResponse()
    {
        Skip.If(_masterKey == null, "MEILI_MASTER_KEY not set; skipping integration test.");

        await EnsureChunkIndexWithDocumentsAsync(new[] { MakeChunk("id-empty", 0, "Title", "content") });

        SearchPageResponse response = await _service.SearchChunksPageAsync("", 1, SearchMode.Keyword);

        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalHits);
    }

    // ── Chunk-to-SearchPageItem mapping ──────────────────────────────────────

    [SkippableFact]
    public async Task SearchChunksPageAsync_FileChunk_MapsPathNotUrl()
    {
        Skip.If(_masterKey == null, "MEILI_MASTER_KEY not set; skipping integration test.");

        string docId = "id-file-mapping";
        await EnsureChunkIndexWithDocumentsAsync(new[] {
            MakeChunk(docId, 0, "File path mapping", "filemapping token abc789", sourceType: "file")
        });

        SearchPageResponse response = await _service.SearchChunksPageAsync("filemapping", 1, SearchMode.Keyword);

        Assert.True(response.TotalHits > 0, "Expected hits.");
        SearchPageItem item = response.Items.First(i => i.Id == ChunkIdentity.Generate(docId, 0));
        Assert.Null(item.Url);
        Assert.NotNull(item.Path);
    }

    [SkippableFact]
    public async Task SearchChunksPageAsync_WebChunk_MapsUrlNotPath()
    {
        Skip.If(_masterKey == null, "MEILI_MASTER_KEY not set; skipping integration test.");

        string docId = "id-web-mapping";
        await EnsureChunkIndexWithDocumentsAsync(new[] {
            MakeChunk(docId, 0, "Web URL mapping", "webmapping token xyz456", sourceType: "web")
        });

        SearchPageResponse response = await _service.SearchChunksPageAsync("webmapping", 1, SearchMode.Keyword);

        Assert.True(response.TotalHits > 0, "Expected hits.");
        SearchPageItem item = response.Items.First(i => i.Id == ChunkIdentity.Generate(docId, 0));
        Assert.NotNull(item.Url);
        Assert.Null(item.Path);
    }

    // ── Hybrid search error handling ─────────────────────────────────────────

    [SkippableFact]
    public async Task SearchChunksPageAsync_HybridMode_EmbedderNotConfigured_ThrowsSearchException()
    {
        Skip.If(_masterKey == null, "MEILI_MASTER_KEY not set; skipping integration test.");

        await EnsureChunkIndexWithDocumentsAsync(new[] { MakeChunk("id-no-emb", 0, "Title", "content") });
        // No embedder configured on the index.

        SearchException ex = await Assert.ThrowsAsync<SearchException>(
            () => _service.SearchChunksPageAsync("content", 1, SearchMode.Hybrid));

        Assert.NotEmpty(ex.Message);
        // Must NOT be ArgumentNullException from null Hits — the error must be surfaced explicitly.
        Assert.IsNotType<ArgumentNullException>(ex.InnerException);
    }

    // ── Japanese query (requires live embedder) ───────────────────────────────

    [SkippableFact]
    public async Task SearchChunksPageAsync_HybridMode_JapaneseQuery_ReturnsResults()
    {
        Skip.If(_masterKey == null, "MEILI_MASTER_KEY not set; skipping integration test.");
        Skip.If(_embedUrl == null, "MEILI_EMBED_URL not set; skipping embedder-dependent test.");

        string docId = "id-ja-hybrid";
        await EnsureChunkIndexWithDocumentsAsync(new[] {
            MakeChunk(docId, 0, "パフォーマンス計測", "フレームレートとメモリ使用量のパフォーマンス計測結果"),
            MakeChunk("other-ja", 0, "無関係なドキュメント", "このチャンクは関係ない")
        });
        await ConfigureEmbedderAsync();

        SearchPageResponse response = await _service.SearchChunksPageAsync("パフォーマンス計測", 1, SearchMode.Hybrid);

        Assert.NotNull(response);
        Assert.NotNull(response.Items);
        Assert.True(response.TotalHits > 0, "Expected hybrid search to return hits for パフォーマンス計測.");
        Assert.Contains(response.Items, i => i.Id == ChunkIdentity.Generate(docId, 0));
    }

    // ── Hybrid search with working embedder (requires live embedder) ──────────

    [SkippableFact]
    public async Task SearchChunksPageAsync_HybridMode_WithEmbedder_ReturnsPopulatedHits()
    {
        Skip.If(_masterKey == null, "MEILI_MASTER_KEY not set; skipping integration test.");
        Skip.If(_embedUrl == null, "MEILI_EMBED_URL not set; skipping embedder-dependent test.");

        string docId = "id-hybrid-hits";
        await EnsureChunkIndexWithDocumentsAsync(new[] {
            MakeChunk(docId, 0, "Hybrid Search Result", "hybrid search integration test unique token hst001"),
            MakeChunk("noise", 0, "Noise Document", "completely unrelated noise noise noise")
        });
        await ConfigureEmbedderAsync();

        SearchPageResponse response = await _service.SearchChunksPageAsync("hybrid search integration", 1, SearchMode.Hybrid);

        Assert.NotNull(response);
        Assert.NotNull(response.Items);
        Assert.True(response.TotalHits > 0, "Hybrid search must return hits.");
        Assert.All(response.Items, item => Assert.NotNull(item.Id));
    }

    [SkippableFact]
    public async Task SearchChunksPageAsync_HybridMode_EmptyResults_HitsIsEmptyNotNull()
    {
        Skip.If(_masterKey == null, "MEILI_MASTER_KEY not set; skipping integration test.");
        Skip.If(_embedUrl == null, "MEILI_EMBED_URL not set; skipping embedder-dependent test.");

        string docId = "id-hybrid-empty";
        await EnsureChunkIndexWithDocumentsAsync(new[] {
            MakeChunk(docId, 0, "Some document", "some content that won't match")
        });
        await ConfigureEmbedderAsync();

        // Query designed not to match anything.
        SearchPageResponse response = await _service.SearchChunksPageAsync("zzzxxx_nomatch_token_9q8r7", 1, SearchMode.Hybrid);

        Assert.Equal(0, response.TotalHits);
        Assert.Empty(response.Items);
    }
}
