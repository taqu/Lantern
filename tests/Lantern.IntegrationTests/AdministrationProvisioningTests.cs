using Lantern.Infrastructure.Persistence;
using Lantern.Web.Administration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lantern.IntegrationTests;

public sealed class AdministrationProvisioningTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lantern-prov-test-" + Guid.NewGuid().ToString("N"));
    private ProvisioningFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new ProvisioningFactory(_directory);
        // Force the application to initialize (runs migrations, provisioning)
        _ = _factory.Services;
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, true);
    }

    private AdministratorAuthentication Auth => _factory.Services.GetRequiredService<AdministratorAuthentication>();

    [Fact]
    public async Task FreshDatabase_NoCredentials_NoAccountCreated()
    {
        await using var db = await _factory.Services.GetRequiredService<IDbContextFactory<LanternDbContext>>().CreateDbContextAsync();
        Assert.False(await db.AdministratorAccounts.AnyAsync());
    }

    [Fact]
    public async Task ProvisionAsync_FreshDatabase_CreatesAccount()
    {
        await Auth.ProvisionAsync("admin", "provisioning-test-password-long");
        await using var db = await _factory.Services.GetRequiredService<IDbContextFactory<LanternDbContext>>().CreateDbContextAsync();
        Assert.True(await db.AdministratorAccounts.AnyAsync());
    }

    [Fact]
    public async Task ProvisionAsync_ExistingAccount_IsPreserved()
    {
        await Auth.ProvisionAsync("admin", "first-provisioning-password");
        await using var db = await _factory.Services.GetRequiredService<IDbContextFactory<LanternDbContext>>().CreateDbContextAsync();
        string firstHash = (await db.AdministratorAccounts.SingleAsync()).PasswordHash;

        // Second call must not overwrite the account
        await Auth.ProvisionAsync("admin", "second-provisioning-password");
        db.ChangeTracker.Clear();
        string secondHash = (await db.AdministratorAccounts.SingleAsync()).PasswordHash;
        Assert.Equal(firstHash, secondHash);
    }

    [Fact]
    public async Task ProvisionAsync_ThenValidate_Succeeds()
    {
        const string password = "correct-provisioned-password";
        await Auth.ProvisionAsync("admin", password);
        var principal = await Auth.ValidateAsync("admin", password);
        Assert.NotNull(principal);
        Assert.True(principal.IsInRole(AdministratorAuthentication.Policy));
    }

    [Fact]
    public async Task ProvisionAsync_WrongPassword_ValidationFails()
    {
        await Auth.ProvisionAsync("admin", "correct-provisioned-password");
        var principal = await Auth.ValidateAsync("admin", "wrong-password-value-here");
        Assert.Null(principal);
    }

    [Fact]
    public async Task ResetPasswordAsync_UpdatesPasswordAndInvalidatesSession()
    {
        await Auth.ProvisionAsync("admin", "original-long-password");
        var principalBefore = await Auth.ValidateAsync("admin", "original-long-password");
        Assert.NotNull(principalBefore);
        string sessionBefore = principalBefore!.FindFirst("session")!.Value;

        await Auth.ResetPasswordAsync("reset-to-this-new-password");

        // Old password no longer works
        Assert.Null(await Auth.ValidateAsync("admin", "original-long-password"));
        // New password works
        var principalAfter = await Auth.ValidateAsync("admin", "reset-to-this-new-password");
        Assert.NotNull(principalAfter);
        // Session stamp has been rotated (old cookie is invalid)
        Assert.NotEqual(sessionBefore, principalAfter!.FindFirst("session")!.Value);
    }

    [Fact]
    public async Task ResetPasswordAsync_NoAccount_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Auth.ResetPasswordAsync("any-valid-long-password-here"));
    }

    [Fact]
    public async Task ResetPasswordAsync_PasswordTooShort_Throws()
    {
        await Auth.ProvisionAsync("admin", "original-long-password");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Auth.ResetPasswordAsync("short"));
    }

    [Fact]
    public async Task ProvisionAsync_NullCredentials_NoAccountCreated()
    {
        await Auth.ProvisionAsync(null, null);
        await using var db = await _factory.Services.GetRequiredService<IDbContextFactory<LanternDbContext>>().CreateDbContextAsync();
        Assert.False(await db.AdministratorAccounts.AnyAsync());
    }

    [Fact]
    public async Task ProvisionAsync_PasswordTooShort_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Auth.ProvisionAsync("admin", "short"));
    }

    [Fact]
    public async Task RepeatedSetup_ExistingCredentials_AccountUnchanged()
    {
        // Simulates running setup-dev.ps1 multiple times with User Secrets already set.
        // ProvisionAsync is called once (account doesn't exist) → account created.
        // ProvisionAsync is called again (account exists) → account unchanged.
        await Auth.ProvisionAsync("admin", "first-setup-run-password");
        await Auth.ProvisionAsync("admin", "second-setup-run-password"); // must not replace

        // Only the first password works
        Assert.NotNull(await Auth.ValidateAsync("admin", "first-setup-run-password"));
        Assert.Null(await Auth.ValidateAsync("admin", "second-setup-run-password"));
    }

    [Fact]
    public async Task LockedAccount_ReleaseAfterReset()
    {
        await Auth.ProvisionAsync("admin", "original-long-password");
        // Trigger lockout with 5 wrong attempts
        for (int i = 0; i < 5; i++)
            await Auth.ValidateAsync("admin", "wrong-attempt-password");
        // Account is now locked
        Assert.Null(await Auth.ValidateAsync("admin", "original-long-password"));

        // Reset clears the lockout
        await Auth.ResetPasswordAsync("unlocked-new-password-after");
        var principal = await Auth.ValidateAsync("admin", "unlocked-new-password-after");
        Assert.NotNull(principal);
    }

    private sealed class ProvisioningFactory(string directory) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.ClearProviders().AddConsole().SetMinimumLevel(LogLevel.Warning));
            builder.UseSetting("Storage:DatabasePath", Path.Combine(directory, "database", "lantern.db"));
            builder.UseSetting("Storage:DataProtectionKeysPath", Path.Combine(directory, "keys"));
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Meilisearch:Url"] = "http://127.0.0.1:1",
                ["Meilisearch:IndexName"] = "prov-test-index",
                ["MeilisearchProcess:Managed"] = "false",
                // Override any DevAdmin User Secrets so tests start with an unpopulated database.
                ["DevAdmin:Username"] = "",
                ["DevAdmin:Password"] = ""
            }));
        }
    }
}
