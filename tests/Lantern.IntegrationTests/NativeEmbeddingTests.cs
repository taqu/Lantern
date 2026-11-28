using Lantern.Core.Crawling;
using Lantern.Core.Search;
using Lantern.Crawling;
using Lantern.Embedding;
using Lantern.Infrastructure;
using Lantern.Infrastructure.Persistence;
using Lantern.Infrastructure.Search;
using Meilisearch;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Lantern.IntegrationTests;

public sealed class NativeEmbeddingTests(ITestOutputHelper output)
{
    [SkippableFact]
    [Trait("Category", "NativeEmbedding")]
    public async Task GraniteProducesStoredVectorsAndSemanticHits()
    {
        string? path = Environment.GetEnvironmentVariable("MODEL_PATH");
        Skip.If(path == null, "Set MODEL_PATH and MEILI_HOST to run native vector indexing.");
        Assert.True(File.Exists(path), $"Missing GGUF: {path}");
        string hostUrl = Environment.GetEnvironmentVariable("MEILI_HOST") ?? throw new InvalidOperationException("Missing MEILI_HOST");
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders().AddSimpleConsole(o => o.TimestampFormat = "HH:mm:ss.fff ").AddFilter("Lantern.Embedding.EmbeddingServer", LogLevel.Debug);
        builder.Services.AddLanternEmbedding(builder.Configuration);
        builder.Services.Configure<EmbeddingOptions>(o => {
            o.ModelPath = path!;
            o.Port = 17703;
        });
        using var host = builder.Build();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        await host.StartAsync(deadline.Token);
        try {
            var state = host.Services.GetRequiredService<EmbeddingServiceStatus>();
            while(state.Status is ModelStatus.Loading or ModelStatus.NotLoaded)
                await Task.Delay(100, deadline.Token);
            Assert.True(state.IsReady, state.Message);
            await state.Listening.WaitAsync(deadline.Token);
            const string endpoint = "http://127.0.0.1:17703/embed";
            using var http = new HttpClient();
            var referenceVectors = new Dictionary<string, float[]>();
            foreach(string text in new[] { "Hello world.", "パフォーマンス計測", "GPU rendering performance analysis.", "リアルタイムレンダリングにおける\nシェーダーの最適化について説明する。" }) {
                var timer = Stopwatch.StartNew();
                using var response = await http.PostAsJsonAsync(endpoint, new { text }, deadline.Token);
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadFromJsonAsync<JsonElement>(deadline.Token);
                var vector = body.GetProperty("embedding").Deserialize<float[]>()!;
                Assert.Equal(384, vector.Length);
                Assert.All(vector, v => Assert.True(float.IsFinite(v)));
                Assert.DoesNotContain(referenceVectors.Values, prior => prior.SequenceEqual(vector));
                referenceVectors.Add(text, vector);
                output.WriteLine($"Inference {timer.ElapsedMilliseconds} ms");
            }

            string uid = $"native-granite-{Guid.NewGuid():N}";
            string? key = Environment.GetEnvironmentVariable("MEILI_MASTER_KEY");
            var client = new MeilisearchClient(hostUrl, key);
            async Task Wait(TaskInfo task) {
                var result = await client.WaitForTaskAsync(task.TaskUid, timeoutMs: 60000, cancellationToken: deadline.Token);
                output.WriteLine($"Index {uid} task {task.TaskUid} {result.Status}, queue {result.StartedAt - result.EnqueuedAt}, processing {result.FinishedAt - result.StartedAt}");
                Assert.True(result.Status == TaskInfoStatus.Succeeded, JsonSerializer.Serialize(result.Error));
            }
            await Wait(await client.CreateIndexAsync(uid, "id"));
            try {
                const string name = "lantern-granite";
                var config = new EmbedderConfigurationService(new TestMeilisearchClientFactory(client),
                    Options.Create(new HybridSearchOptions { ChunkIndexName = uid, EmbedderName = name }),
                    Options.Create(new MeilisearchOptions()), NullLogger<EmbedderConfigurationService>.Instance);
                // Simulate persisted settings from a deployment with a different request schema.
                var stale = new Embedder {
                    Source = EmbedderSource.Rest, Url = endpoint, Dimensions = 384,
                    Request = new Dictionary<string, object> { ["input"] = "{{text}}" },
                    Response = new Dictionary<string, object> { ["embedding"] = "{{embedding}}" },
                    DocumentTemplate = "{{doc.title}}\n{{doc.sectionTitle}}\n{{doc.content}}",
                    DocumentTemplateMaxBytes = 8192
                };
                var index = client.Index(uid);
                await Wait(await index.UpdateEmbeddersAsync(new Dictionary<string, Embedder> { [name] = stale }));
                var sample = new[] { new { id = "gpu", title = "描画", sectionTitle = "GPU", content = "Shader optimization reduces rendering time." } };
                var brokenTask = await index.AddDocumentsAsync(sample);
                var failure = await client.WaitForTaskAsync(brokenTask.TaskUid, timeoutMs: 60000, cancellationToken: deadline.Token);
                Assert.Equal(TaskInfoStatus.Failed, failure.Status);
                Assert.Equal("vector_embedding_error", failure.Error["code"]);
                output.WriteLine($"Controlled stale-template failure {brokenTask.TaskUid}: {failure.Error["message"]}");
                await config.ConfigureEmbedderAsync(endpoint, deadline.Token);
                await config.VerifyConnectionAsync(deadline.Token);
                var effective = (await client.Index(uid).GetEmbeddersAsync())[name];
                Assert.Equal(EmbedderSource.Rest, effective.Source);
                Assert.Equal(endpoint, effective.Url);
                Assert.Equal(8192, effective.DocumentTemplateMaxBytes);
                Assert.Equal("{{text}}", JsonSerializer.SerializeToElement(effective.Request).GetProperty("text").GetString());
                Assert.Equal("{{embedding}}", JsonSerializer.SerializeToElement(effective.Response).GetProperty("embedding").GetString());
                state.SetFailed("controlled model unavailability");
                try {
                    var unavailable = await index.AddDocumentsAsync(sample);
                    var failed = await client.WaitForTaskAsync(unavailable.TaskUid, timeoutMs: 540000, cancellationToken: deadline.Token);
                    Assert.Equal(TaskInfoStatus.Failed, failed.Status);
                    Assert.Equal("vector_embedding_error", failed.Error["code"]);
                    output.WriteLine($"Controlled HTTP 503 failure {unavailable.TaskUid}: {failed.Error["message"]}");
                } finally {
                    state.SetReady();
                }
                await Wait(await index.AddDocumentsAsync(sample));
                using var meili = new HttpClient { BaseAddress = new Uri(hostUrl) };
                meili.DefaultRequestHeaders.Authorization = new("Bearer", key);
                var doc = await meili.GetFromJsonAsync<JsonElement>($"/indexes/{uid}/documents/gpu?retrieveVectors=true", deadline.Token);
                Assert.Equal(384, doc.GetProperty("_vectors").GetProperty(name).GetProperty("embeddings")[0].GetArrayLength());
                using var search = await meili.PostAsJsonAsync($"/indexes/{uid}/search", new { q = "faster graphics", hybrid = new { embedder = name, semanticRatio = 1.0 } }, deadline.Token);
                search.EnsureSuccessStatusCode();
                var results = await search.Content.ReadFromJsonAsync<JsonElement>(deadline.Token);
                Assert.Equal(1, results.GetProperty("semanticHitCount").GetInt32());
                Assert.Equal("gpu", results.GetProperty("hits")[0].GetProperty("id").GetString());
                async Task IndexGroup(int group)
                {
                    var chunks = Enumerable.Range(0, 4).Select(i => new {
                        id = $"group-{group}-{i}", title = "計測", sectionTitle = $"試験 {i}",
                        content = string.Concat(Enumerable.Repeat($"描画性能を計測する。{group} {i} ", 70))
                    }).ToArray();
                    await Wait(await client.Index(uid).AddDocumentsAsync(chunks));
                }
                await VerifyCrawlerAsync(client, uid, name, deadline.Token);
                foreach(int count in new[] { 2, 4, 32 }) {
                    var requests = Enumerable.Range(0, count).Select(async i => {
                        var reference = referenceVectors.ElementAt(i % referenceVectors.Count);
                        using var response = await http.PostAsJsonAsync(endpoint, new { text = reference.Key }, deadline.Token);
                        response.EnsureSuccessStatusCode();
                        var body = await response.Content.ReadFromJsonAsync<JsonElement>(deadline.Token);
                        Assert.Equal(reference.Value, body.GetProperty("embedding").Deserialize<float[]>());
                    });
                    await Task.WhenAll(requests);
                }
                using var oversized = await http.PostAsJsonAsync(endpoint, new { text = new string('x', 32769) }, deadline.Token);
                Assert.Equal(System.Net.HttpStatusCode.BadRequest, oversized.StatusCode);
                await IndexGroup(0);
                await Task.WhenAll(IndexGroup(1), IndexGroup(2));
            } finally {
                await client.DeleteIndexAsync(uid);
            }
        } finally {
            await host.StopAsync();
        }
    }

    private static async Task VerifyCrawlerAsync(MeilisearchClient client, string uid, string name, CancellationToken ct)
    {
        var root = Directory.CreateTempSubdirectory("lantern-native-");
        string parentIndex = uid + "-documents";
        try {
            string files = Directory.CreateDirectory(Path.Combine(root.FullName, "files")).FullName;
            foreach(string file in new[] { "rendering.pdf", "テスト.pptx" })
                File.Copy(Path.Combine(AppContext.BaseDirectory, "data", file), Path.Combine(files, file));
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["Meilisearch:IndexName"] = parentIndex,
                ["HybridSearch:ChunkIndexName"] = uid,
                ["HybridSearch:EmbedderName"] = name,
                ["HybridSearch:Enabled"] = "true",
                ["HybridSearch:SemanticRatio"] = "1"
            }).Build();
            await using var services = new ServiceCollection().AddLogging()
                .AddDbContextFactory<LanternDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(root.FullName, "test.db")};Pooling=False"))
                .AddLanternInfrastructure(config).AddLanternCrawling(config)
                .AddSingleton<IMeilisearchClientFactory>(new TestMeilisearchClientFactory(client))
                .BuildServiceProvider();
            await using var db = await services.GetRequiredService<IDbContextFactory<LanternDbContext>>().CreateDbContextAsync(ct);
            await db.Database.MigrateAsync(ct);
            var crawl = await services.GetRequiredService<IFileCrawler>().CrawlAsync(new FileCrawlerConfiguration {
                CrawlerId = Guid.NewGuid(), Name = "embedding test", RootPath = files,
                IncludedExtensions = [".pdf", ".pptx"], MaxConcurrentFiles = 1
            }, ct);
            Assert.Equal(2, crawl.IndexedFiles);
            var repository = services.GetRequiredService<ICrawlStateRepository>();
            var pending = await repository.FindPendingIndexingAsync(5, ct);
            Assert.Equal(2, pending.Count);
            foreach(var file in pending) {
                var result = await client.WaitForTaskAsync(file.PendingMeilisearchTaskUid!.Value, timeoutMs: 60000, cancellationToken: ct);
                Assert.True(result.Status == TaskInfoStatus.Succeeded, JsonSerializer.Serialize(result.Error));
                var search = services.GetRequiredService<ISearchService>();
                var hits = await search.SearchChunksPageAsync("速さ", 1, SearchMode.Hybrid,
                    new SearchFilters { Extension = Path.GetExtension(file.CanonicalPath) }, ct);
                Assert.Contains(hits.Items, hit => string.Equals(hit.Path, file.CanonicalPath, StringComparison.OrdinalIgnoreCase));
            }
            await using(var web = new WebApplicationFactory<Program>().WithWebHostBuilder(web => {
                web.UseEnvironment("Testing");
                web.UseSetting("Storage:DatabasePath", Path.Combine(root.FullName, "public.db"));
                web.UseSetting("Storage:DataProtectionKeysPath", Path.Combine(root.FullName, "keys"));
                web.UseSetting("MeilisearchProcess:Managed", "false");
                web.UseSetting("Embedding:Enabled", "false");
                web.ConfigureLogging(logging => logging.ClearProviders().AddConsole().SetMinimumLevel(LogLevel.Warning));
                web.ConfigureAppConfiguration((_, configuration) => configuration.AddConfiguration(config));
                web.ConfigureServices(registrations => {
                    registrations.AddDataProtection().UseEphemeralDataProtectionProvider();
                    registrations.AddSingleton<IMeilisearchClientFactory>(new TestMeilisearchClientFactory(client));
                    registrations.AddSingleton<IMeilisearchCredentialManager>(new ReadyCredentials());
                });
            })) {
                using var anonymous = web.CreateClient(new WebApplicationFactoryClientOptions {
                    BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = false
                });
                string keyword = await anonymous.GetStringAsync("/search?q=Rendering", ct);
                Assert.Contains("rendering.pdf", keyword, StringComparison.OrdinalIgnoreCase);
                string hybrid = await anonymous.GetStringAsync("/search?q=faster%20graphics&mode=hybrid", ct);
                Assert.Contains("Copy file path", hybrid);
                Assert.Contains("rendering.pdf", hybrid, StringComparison.OrdinalIgnoreCase);
            }
            var invalid = await client.Index(uid).AddDocumentsAsync(new[] { new { title = "missing document ID" } });
            var terminal = await client.WaitForTaskAsync(invalid.TaskUid, timeoutMs: 60000, cancellationToken: ct);
            Assert.Equal(TaskInfoStatus.Failed, terminal.Status);
            var failed = new FileState {
                CrawlerId = Guid.NewGuid(), CanonicalPath = "invalid.txt", DocumentId = "invalid-fixture",
                SyncStatus = CrawlSyncStatus.IndexingPending, PendingMeilisearchTaskUid = invalid.TaskUid
            };
            await repository.SaveAsync(failed, ct);
            using var reconciliation = new MeilisearchTaskReconciliationService(repository,
                new TestMeilisearchClientFactory(client), new ReadyCredentials(), Options.Create(new MeilisearchOptions()),
                NullLogger<MeilisearchTaskReconciliationService>.Instance);
            await reconciliation.StartAsync(ct);
            while((await repository.FindPendingIndexingAsync(5, ct)).Count != 0)
                await Task.Delay(50, ct);
            await reconciliation.StopAsync(ct);
            foreach(var file in pending)
                Assert.Equal(CrawlSyncStatus.Indexed, (await repository.FindAsync(file.CrawlerId, file.CanonicalPath, ct))!.SyncStatus);
            var persisted = await repository.FindAsync(failed.CrawlerId, failed.CanonicalPath, ct);
            Assert.Equal(CrawlSyncStatus.Failed, persisted!.SyncStatus);
            Assert.Equal(failed.DocumentId, persisted.DocumentId);
            Assert.Contains(invalid.TaskUid.ToString(), persisted.LastError);
            Assert.Contains(terminal.Error["code"], persisted.LastError);
        } finally {
            await client.DeleteIndexAsync(parentIndex);
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder {
                DataSource = Path.Combine(root.FullName, "public.db"), ForeignKeys = true
            }.ToString());
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
            root.Delete(true);
        }
    }

    private sealed class ReadyCredentials : IMeilisearchCredentialManager
    {
        public bool IsReady => true;
        public Task EnsureCredentialsAsync(CancellationToken ct) => Task.CompletedTask;
        public MeilisearchCredential GetCredential(MeilisearchCredentialRole role) => throw new NotSupportedException();
    }
}
