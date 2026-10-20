using Lantern.Core.Crawling;
using Lantern.Core.Administration;
using Lantern.Core.Search;
using Lantern.Crawling;
using Lantern.Crawling.Files;
using Lantern.Infrastructure;
using Lantern.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lantern.IntegrationTests.Crawling;

public sealed class FileCrawlerIntegrationTests : IAsyncLifetime
{
    private readonly string _dbDir = Path.Combine(Path.GetTempPath(), "lantern-crawler-test-" + Guid.NewGuid().ToString("N"));
    private readonly string _filesDir = Path.Combine(Path.GetTempPath(), "lantern-crawl-files-" + Guid.NewGuid().ToString("N"));
    private ServiceProvider _services = null!;

    private IFileCrawler Crawler => _services.GetRequiredService<IFileCrawler>();

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_dbDir);
        Directory.CreateDirectory(_filesDir);

        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Meilisearch:Url"] = "http://127.0.0.1:7700",
                ["Meilisearch:IndexName"] = "lantern-crawler-test-" + Guid.NewGuid().ToString("N")[..8],
                ["HybridSearch:ChunkIndexName"] = "lantern-chunks-test-" + Guid.NewGuid().ToString("N")[..8],
                ["FileCrawling:QueueCapacity"] = "8"
            })
            .Build();

        _services = new ServiceCollection()
            .AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning))
            .AddDbContextFactory<LanternDbContext>(o =>
                o.UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(_dbDir, "test.db"),
                    Pooling = false
                }.ToString()))
            .AddLanternInfrastructure(config)
            .AddLanternCrawling(config)
            .BuildServiceProvider();

        await using LanternDbContext db = await _services.GetRequiredService<IDbContextFactory<LanternDbContext>>().CreateDbContextAsync();
        await db.Database.MigrateAsync();

        try
        {
            await _services.GetRequiredService<IDocumentIndexingService>().EnsureIndexAsync();
            await _services.GetRequiredService<IChunkIndexingService>().EnsureIndexAsync();
        }
        catch { /* Meilisearch unavailable; Meilisearch-dependent tests will be skipped */ }
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        Directory.Delete(_dbDir, true);
        Directory.Delete(_filesDir, true);
    }

    private FileCrawlerConfiguration MakeConfig(
        IReadOnlyList<string>? extensions = null,
        IReadOnlyList<string>? excluded = null,
        bool recursive = true) =>
        new()
        {
            CrawlerId = Guid.NewGuid(),
            Name = "Test Crawler",
            RootPath = _filesDir,
            Recursive = recursive,
            IncludedExtensions = extensions ?? [".txt", ".md"],
            ExcludedDirectories = excluded ?? [],
            MaxFileSizeMB = 10,
            MaxConcurrentFiles = 2
        };

    [SkippableFact]
    public async Task InitialCrawlIndexesDiscoveredFiles()
    {
        ISearchEngineHealthService health = _services.GetRequiredService<ISearchEngineHealthService>();
        Skip.IfNot((await health.GetAsync(refresh: true)).Available, "Meilisearch unavailable");

        File.WriteAllText(Path.Combine(_filesDir, "notes.txt"), "Meeting notes from today's standup.");
        File.WriteAllText(Path.Combine(_filesDir, "readme.md"), "# Project\nThis is the readme.");

        FileCrawlerConfiguration config = MakeConfig();
        FileCrawlResult result = await Crawler.CrawlAsync(config);

        Assert.Equal(CrawlStatus.Completed, result.Status);
        Assert.Equal(2, result.DiscoveredFiles);
        Assert.Equal(2, result.IndexedFiles);
        Assert.Equal(0, result.UnchangedFiles);
    }

    [SkippableFact]
    public async Task SecondCrawlWithNoChangesSkipsProcessing()
    {
        ISearchEngineHealthService health = _services.GetRequiredService<ISearchEngineHealthService>();
        Skip.IfNot((await health.GetAsync(refresh: true)).Available, "Meilisearch unavailable");

        File.WriteAllText(Path.Combine(_filesDir, "stable.txt"), "Stable content.");
        FileCrawlerConfiguration config = MakeConfig();

        FileCrawlResult first = await Crawler.CrawlAsync(config);
        Assert.Equal(CrawlStatus.Completed, first.Status);

        FileCrawlResult second = await Crawler.CrawlAsync(config);
        Assert.Equal(CrawlStatus.Completed, second.Status);
        Assert.Equal(0, second.IndexedFiles);
        Assert.Equal(first.DiscoveredFiles, second.UnchangedFiles);
    }

    [SkippableFact]
    public async Task ModifiedFileIsReindexed()
    {
        ISearchEngineHealthService health = _services.GetRequiredService<ISearchEngineHealthService>();
        Skip.IfNot((await health.GetAsync(refresh: true)).Available, "Meilisearch unavailable");

        string filePath = Path.Combine(_filesDir, "report.txt");
        File.WriteAllText(filePath, "Original content.");
        FileCrawlerConfiguration config = MakeConfig();

        await Crawler.CrawlAsync(config);

        // Modify the file (ensure different timestamp)
        await Task.Delay(100);
        File.WriteAllText(filePath, "Updated content with new information.");
        new FileInfo(filePath).LastWriteTimeUtc = DateTime.UtcNow;

        FileCrawlResult second = await Crawler.CrawlAsync(config);
        Assert.Equal(CrawlStatus.Completed, second.Status);
        Assert.True(second.ProcessedFiles >= 1);
    }

    [SkippableFact]
    public async Task DeletedFileIsRemovedAfterCompleteScan()
    {
        ISearchEngineHealthService health = _services.GetRequiredService<ISearchEngineHealthService>();
        Skip.IfNot((await health.GetAsync(refresh: true)).Available, "Meilisearch unavailable");

        string filePath = Path.Combine(_filesDir, "to_delete.txt");
        File.WriteAllText(filePath, "Will be deleted.");
        FileCrawlerConfiguration config = MakeConfig();

        await Crawler.CrawlAsync(config);
        File.Delete(filePath);

        FileCrawlResult second = await Crawler.CrawlAsync(config);
        Assert.Equal(CrawlStatus.Completed, second.Status);
        Assert.Equal(1, second.DeletedFiles);
    }

    [SkippableFact]
    public async Task RepeatedCrawlsDoNotCreateDuplicates()
    {
        ISearchEngineHealthService health = _services.GetRequiredService<ISearchEngineHealthService>();
        Skip.IfNot((await health.GetAsync(refresh: true)).Available, "Meilisearch unavailable");

        File.WriteAllText(Path.Combine(_filesDir, "doc.txt"), "Content.");
        FileCrawlerConfiguration config = MakeConfig();

        await Crawler.CrawlAsync(config);
        await Crawler.CrawlAsync(config);
        FileCrawlResult third = await Crawler.CrawlAsync(config);

        // All subsequent runs should be unchanged (no new indexing)
        Assert.Equal(CrawlStatus.Completed, third.Status);
        Assert.Equal(0, third.IndexedFiles);
        Assert.True(third.UnchangedFiles > 0);
    }

    [Fact]
    public async Task InvalidRootPathReturnsFailed()
    {
        var config = new FileCrawlerConfiguration
        {
            CrawlerId = Guid.NewGuid(),
            RootPath = @"Z:\NonExistentPath\That\Does\Not\Exist",
            IncludedExtensions = [".txt"],
            MaxConcurrentFiles = 1
        };

        FileCrawlResult result = await Crawler.CrawlAsync(config);
        Assert.Equal(CrawlStatus.Failed, result.Status);
    }

    [Fact]
    public async Task CancellationReturnsCanceledStatus()
    {
        for (int i = 0; i < 5; i++)
            File.WriteAllText(Path.Combine(_filesDir, $"file{i}.txt"), "content " + i);

        FileCrawlerConfiguration config = MakeConfig();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        FileCrawlResult result = await Crawler.CrawlAsync(config, cts.Token);
        Assert.Equal(CrawlStatus.Canceled, result.Status);
    }

    [Fact]
    public async Task FilesInExcludedDirectoryAreNotIndexed()
    {
        DirectoryInfo excluded = Directory.CreateDirectory(Path.Combine(_filesDir, "excluded"));
        File.WriteAllText(Path.Combine(_filesDir, "included.txt"), "Included.");
        File.WriteAllText(Path.Combine(excluded.FullName, "ignored.txt"), "Excluded.");

        FileCrawlerConfiguration config = MakeConfig(excluded: ["excluded"]);
        FileCrawlResult result = await Crawler.CrawlAsync(config);

        Assert.Equal(1, result.DiscoveredFiles);
    }
}
