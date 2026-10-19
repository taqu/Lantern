using System.ComponentModel.DataAnnotations;
using Lantern.Core.Administration;
using Lantern.Infrastructure.Persistence;
using Lantern.Web.Administration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lantern.IntegrationTests;

public sealed class SqlitePersistenceTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lantern-sqlite-test-" + Guid.NewGuid().ToString("N"));
    private ServiceProvider _services = null!;
    private IDbContextFactory<LanternDbContext> Factory => _services.GetRequiredService<IDbContextFactory<LanternDbContext>>();

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        _services = new ServiceCollection().AddDbContextFactory<LanternDbContext>(options =>
            options.UseSqlite(new SqliteConnectionStringBuilder { DataSource = Path.Combine(_directory, "test.db"), Pooling = false }.ToString())).BuildServiceProvider();
        await using var db = await Factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        Directory.Delete(_directory, true);
    }

    [Fact]
    public async Task MigrationSettingsUpdatesAndRepeatedInitializationPreserveData()
    {
        var service = new ApplicationSettingsService(Factory);
        Assert.Equal("Lantern", (await service.GetAsync()).DisplayName);
        await service.SaveAsync(new ApplicationSetting { DisplayName = "First" });
        await service.SaveAsync(new ApplicationSetting { DisplayName = "社内 Lantern" });
        await using (var db = await Factory.CreateDbContextAsync())
        {
            Assert.Single(await db.Database.GetAppliedMigrationsAsync());
            Assert.False(db.Database.HasPendingModelChanges());
            await db.Database.MigrateAsync();
        }
        Assert.Equal("社内 Lantern", (await new ApplicationSettingsService(Factory).GetAsync()).DisplayName);
        await Assert.ThrowsAsync<ValidationException>(() => service.SaveAsync(new ApplicationSetting { DisplayName = " " }));
        Assert.Equal("社内 Lantern", (await service.GetAsync()).DisplayName);
    }

    [Fact]
    public async Task CrawlersPersistAndDatabaseConstraintsRejectInvalidData()
    {
        var crawler = new CrawlerDefinition { Name = "Network documents", CrawlerType = "file" };
        await using (var db = await Factory.CreateDbContextAsync())
        { db.CrawlerDefinitions.Add(crawler); await db.SaveChangesAsync(); }
        await using (var db = await Factory.CreateDbContextAsync())
        {
            Assert.Equal(crawler.Id, (await db.CrawlerDefinitions.SingleAsync()).Id);
            await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE CrawlerDefinitions SET CrawlerType = 'invalid'"));
            await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
                "INSERT INTO ApplicationSettings (Id, DisplayName) VALUES (2, 'Invalid singleton')"));
        }
    }

    [Fact]
    public async Task AdministratorProvisioningHashValidationLockoutAndRevocation()
    {
        var auth = new AdministratorAuthentication(Factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() => auth.ProvisionAsync("admin", "short"));
        await auth.ProvisionAsync("admin", "a-long-initial-password");
        // Existing accounts cannot be taken over with new bootstrap configuration.
        await auth.ProvisionAsync("attacker", "a-different-initial-password");
        Assert.Null(await auth.ValidateAsync("attacker", "a-different-initial-password"));
        var principal = await auth.ValidateAsync("admin", "a-long-initial-password");
        Assert.NotNull(principal);
        Assert.True(await auth.IsSessionValidAsync(principal));
        await auth.RevokeAsync();
        Assert.False(await auth.IsSessionValidAsync(principal));
        for (var i = 0; i < 5; i++) Assert.Null(await auth.ValidateAsync("admin", "wrong-password"));
        Assert.Null(await new AdministratorAuthentication(Factory).ValidateAsync("admin", "a-long-initial-password"));
        await using var db = await Factory.CreateDbContextAsync();
        var account = await db.AdministratorAccounts.SingleAsync();
        Assert.NotEqual("a-long-initial-password", account.PasswordHash);
        Assert.NotNull(account.LockedUntil);
    }
}
