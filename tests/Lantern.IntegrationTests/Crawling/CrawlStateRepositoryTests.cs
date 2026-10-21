using Lantern.Core.Crawling;
using Lantern.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lantern.IntegrationTests.Crawling;

public sealed class CrawlStateRepositoryTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lantern-state-test-" + Guid.NewGuid().ToString("N"));
    private ServiceProvider _services = null!;

    private IDbContextFactory<LanternDbContext> Factory =>
        _services.GetRequiredService<IDbContextFactory<LanternDbContext>>();

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        _services = new ServiceCollection()
            .AddDbContextFactory<LanternDbContext>(o =>
                o.UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(_dir, "test.db"),
                    Pooling = false
                }.ToString()))
            .BuildServiceProvider();
        await using LanternDbContext db = await Factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        Directory.Delete(_dir, true);
    }

    private CrawlStateRepository CreateRepo() => new(Factory);

    [Fact]
    public async Task SaveAndFindRoundTrip()
    {
        var crawlerId = Guid.NewGuid();
        CrawlStateRepository repo = CreateRepo();
        var state = new FileState
        {
            CrawlerId = crawlerId,
            DocumentId = "doc1",
            CanonicalPath = @"C:\docs\report.pdf",
            FileSize = 1024,
            LastWriteTimeUtc = DateTimeOffset.UtcNow,
            LastSeenScanId = Guid.NewGuid(),
            SyncStatus = CrawlSyncStatus.Indexed
        };

        await repo.SaveAsync(state);
        FileState? found = await repo.FindAsync(crawlerId, @"C:\docs\report.pdf");

        Assert.NotNull(found);
        Assert.Equal("doc1", found.DocumentId);
        Assert.Equal(CrawlSyncStatus.Indexed, found.SyncStatus);
    }

    [Fact]
    public async Task SaveUpdatesExistingRecord()
    {
        var crawlerId = Guid.NewGuid();
        CrawlStateRepository repo = CreateRepo();
        var state = new FileState
        {
            CrawlerId = crawlerId,
            DocumentId = "doc1",
            CanonicalPath = @"C:\docs\file.txt",
            FileSize = 100,
            LastWriteTimeUtc = DateTimeOffset.UtcNow,
            LastSeenScanId = Guid.NewGuid(),
            SyncStatus = CrawlSyncStatus.Indexed
        };
        await repo.SaveAsync(state);

        state.SyncStatus = CrawlSyncStatus.Failed;
        state.LastError = "Extraction failed";
        await repo.SaveAsync(state);

        FileState? found = await repo.FindAsync(crawlerId, @"C:\docs\file.txt");
        Assert.NotNull(found);
        Assert.Equal(CrawlSyncStatus.Failed, found.SyncStatus);
        Assert.Equal("Extraction failed", found.LastError);
    }

    [Fact]
    public async Task GetNotSeenInScanExcludesCurrentScan()
    {
        var crawlerId = Guid.NewGuid();
        CrawlStateRepository repo = CreateRepo();
        var oldScan = Guid.NewGuid();
        var newScan = Guid.NewGuid();

        await repo.SaveAsync(new FileState
        {
            CrawlerId = crawlerId,
            DocumentId = "old",
            CanonicalPath = @"C:\old.txt",
            FileSize = 10,
            LastWriteTimeUtc = DateTimeOffset.UtcNow,
            LastSeenScanId = oldScan,
            SyncStatus = CrawlSyncStatus.Indexed
        });
        await repo.SaveAsync(new FileState
        {
            CrawlerId = crawlerId,
            DocumentId = "current",
            CanonicalPath = @"C:\current.txt",
            FileSize = 10,
            LastWriteTimeUtc = DateTimeOffset.UtcNow,
            LastSeenScanId = newScan,
            SyncStatus = CrawlSyncStatus.Indexed
        });

        IReadOnlyList<FileState> notSeen = await repo.GetNotSeenInScanAsync(crawlerId, newScan);

        Assert.Single(notSeen);
        Assert.Equal("old", notSeen[0].DocumentId);
    }

    [Fact]
    public async Task GetNotSeenExcludesFailedRecords()
    {
        var crawlerId = Guid.NewGuid();
        CrawlStateRepository repo = CreateRepo();
        var oldScan = Guid.NewGuid();
        var newScan = Guid.NewGuid();

        await repo.SaveAsync(new FileState
        {
            CrawlerId = crawlerId,
            DocumentId = "failed",
            CanonicalPath = @"C:\failed.txt",
            FileSize = 10,
            LastWriteTimeUtc = DateTimeOffset.UtcNow,
            LastSeenScanId = oldScan,
            SyncStatus = CrawlSyncStatus.Failed
        });

        IReadOnlyList<FileState> notSeen = await repo.GetNotSeenInScanAsync(crawlerId, newScan);
        Assert.Empty(notSeen);
    }

    [Fact]
    public async Task DeleteRemovesRecord()
    {
        var crawlerId = Guid.NewGuid();
        CrawlStateRepository repo = CreateRepo();
        var state = new FileState
        {
            CrawlerId = crawlerId,
            DocumentId = "doc",
            CanonicalPath = @"C:\del.txt",
            FileSize = 10,
            LastWriteTimeUtc = DateTimeOffset.UtcNow,
            LastSeenScanId = Guid.NewGuid(),
            SyncStatus = CrawlSyncStatus.Indexed
        };
        await repo.SaveAsync(state);
        Assert.True(state.Id > 0);

        await repo.DeleteAsync(state.Id);
        FileState? found = await repo.FindAsync(crawlerId, @"C:\del.txt");
        Assert.Null(found);
    }

    [Fact]
    public async Task StatePersistsAcrossContextInstances()
    {
        var crawlerId = Guid.NewGuid();
        string path = @"C:\persist.txt";

        await CreateRepo().SaveAsync(new FileState
        {
            CrawlerId = crawlerId,
            DocumentId = "doc",
            CanonicalPath = path,
            FileSize = 42,
            LastWriteTimeUtc = DateTimeOffset.UtcNow,
            LastSeenScanId = Guid.NewGuid(),
            SyncStatus = CrawlSyncStatus.Indexed
        });

        FileState? found = await CreateRepo().FindAsync(crawlerId, path);
        Assert.NotNull(found);
        Assert.Equal(42, found.FileSize);
    }

    [Fact]
    public async Task GetNotSeenIsScopedToCrawler()
    {
        var crawler1 = Guid.NewGuid();
        var crawler2 = Guid.NewGuid();
        CrawlStateRepository repo = CreateRepo();
        var oldScan = Guid.NewGuid();
        var newScan = Guid.NewGuid();

        await repo.SaveAsync(new FileState
        {
            CrawlerId = crawler1,
            DocumentId = "doc1",
            CanonicalPath = @"C:\a.txt",
            FileSize = 10,
            LastWriteTimeUtc = DateTimeOffset.UtcNow,
            LastSeenScanId = oldScan,
            SyncStatus = CrawlSyncStatus.Indexed
        });
        await repo.SaveAsync(new FileState
        {
            CrawlerId = crawler2,
            DocumentId = "doc2",
            CanonicalPath = @"C:\b.txt",
            FileSize = 10,
            LastWriteTimeUtc = DateTimeOffset.UtcNow,
            LastSeenScanId = oldScan,
            SyncStatus = CrawlSyncStatus.Indexed
        });

        IReadOnlyList<FileState> notSeen = await repo.GetNotSeenInScanAsync(crawler1, newScan);
        Assert.Single(notSeen);
        Assert.Equal("doc1", notSeen[0].DocumentId);
    }
}
