using Lantern.Core.Crawling;
using Lantern.Crawling.Files;
using Lantern.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lantern.IntegrationTests.Crawling;

public sealed class FileFingerprintTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lantern-fp-test-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public async Task NewFileIsDetectedAsNew()
    {
        var crawlerId = Guid.NewGuid();
        var repo = new CrawlStateRepository(Factory);
        string path = @"C:\docs\new.txt";

        FileState? existing = await repo.FindAsync(crawlerId, path);
        Assert.Null(existing);
    }

    [Fact]
    public async Task UnchangedFileIsDetectedByFingerprint()
    {
        var crawlerId = Guid.NewGuid();
        var repo = new CrawlStateRepository(Factory);
        string path = @"C:\docs\unchanged.txt";
        DateTimeOffset lastWrite = DateTimeOffset.UtcNow.AddHours(-1);
        const long size = 2048;

        await repo.SaveAsync(new FileState
        {
            CrawlerId = crawlerId,
            DocumentId = "doc",
            CanonicalPath = path,
            FileSize = size,
            LastWriteTimeUtc = lastWrite,
            LastSeenScanId = Guid.NewGuid(),
            SyncStatus = CrawlSyncStatus.Indexed
        });

        var discovered = new DiscoveredFile(path, size, lastWrite);
        FileState? existing = await repo.FindAsync(crawlerId, discovered.CanonicalPath);
        Assert.NotNull(existing);

        bool unchanged = existing.SyncStatus == CrawlSyncStatus.Indexed
            && existing.FileSize == discovered.FileSize
            && existing.LastWriteTimeUtc == discovered.LastWriteTimeUtc;
        Assert.True(unchanged);
    }

    [Fact]
    public async Task ChangedFileSizeIsDetectedAsChanged()
    {
        var crawlerId = Guid.NewGuid();
        var repo = new CrawlStateRepository(Factory);
        string path = @"C:\docs\changed.txt";
        DateTimeOffset lastWrite = DateTimeOffset.UtcNow.AddHours(-1);

        await repo.SaveAsync(new FileState
        {
            CrawlerId = crawlerId,
            DocumentId = "doc",
            CanonicalPath = path,
            FileSize = 1000,
            LastWriteTimeUtc = lastWrite,
            LastSeenScanId = Guid.NewGuid(),
            SyncStatus = CrawlSyncStatus.Indexed
        });

        var discovered = new DiscoveredFile(path, 2000, lastWrite); // different size
        FileState? existing = await repo.FindAsync(crawlerId, discovered.CanonicalPath);
        Assert.NotNull(existing);

        bool unchanged = existing.SyncStatus == CrawlSyncStatus.Indexed
            && existing.FileSize == discovered.FileSize
            && existing.LastWriteTimeUtc == discovered.LastWriteTimeUtc;
        Assert.False(unchanged);
    }

    [Fact]
    public async Task ChangedTimestampIsDetectedAsChanged()
    {
        var crawlerId = Guid.NewGuid();
        var repo = new CrawlStateRepository(Factory);
        string path = @"C:\docs\modified.txt";
        const long size = 1000;

        await repo.SaveAsync(new FileState
        {
            CrawlerId = crawlerId,
            DocumentId = "doc",
            CanonicalPath = path,
            FileSize = size,
            LastWriteTimeUtc = DateTimeOffset.UtcNow.AddHours(-2),
            LastSeenScanId = Guid.NewGuid(),
            SyncStatus = CrawlSyncStatus.Indexed
        });

        var discovered = new DiscoveredFile(path, size, DateTimeOffset.UtcNow.AddHours(-1));
        FileState? existing = await repo.FindAsync(crawlerId, discovered.CanonicalPath);
        Assert.NotNull(existing);

        bool unchanged = existing.SyncStatus == CrawlSyncStatus.Indexed
            && existing.FileSize == discovered.FileSize
            && existing.LastWriteTimeUtc == discovered.LastWriteTimeUtc;
        Assert.False(unchanged);
    }

    [Fact]
    public async Task FailedFileIsRetriedNextScan()
    {
        var crawlerId = Guid.NewGuid();
        var repo = new CrawlStateRepository(Factory);
        string path = @"C:\docs\failed.txt";
        const long size = 1000;
        DateTimeOffset lastWrite = DateTimeOffset.UtcNow.AddHours(-1);

        await repo.SaveAsync(new FileState
        {
            CrawlerId = crawlerId,
            DocumentId = "doc",
            CanonicalPath = path,
            FileSize = size,
            LastWriteTimeUtc = lastWrite,
            LastSeenScanId = Guid.NewGuid(),
            SyncStatus = CrawlSyncStatus.Failed,
            LastError = "Previous failure"
        });

        var discovered = new DiscoveredFile(path, size, lastWrite);
        FileState? existing = await repo.FindAsync(crawlerId, discovered.CanonicalPath);
        Assert.NotNull(existing);

        // Failed files should NOT be considered unchanged — always retry
        bool unchanged = existing.SyncStatus == CrawlSyncStatus.Indexed
            && existing.FileSize == discovered.FileSize
            && existing.LastWriteTimeUtc == discovered.LastWriteTimeUtc;
        Assert.False(unchanged);
    }
}
