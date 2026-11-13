using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lantern.IntegrationTests;

/// <summary>
/// Verifies that ASP.NET Core Data Protection is configured correctly in Lantern:
/// keys are stored to the filesystem, persist across host restarts, and operate
/// independently of Meilisearch credential files.
/// </summary>
public sealed class DataProtectionTests : IAsyncDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "lantern-dp-test-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void DataProtection_ProtectAndUnprotect_RoundTrip()
    {
        using DpFactory factory = new DpFactory(_dir);
        IDataProtector dp = factory.Services
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Lantern.Test.Credential");

        const string plaintext = "test-meilisearch-api-key";
        string ciphertext = dp.Protect(plaintext);

        Assert.NotEqual(plaintext, ciphertext);
        Assert.Equal(plaintext, dp.Unprotect(ciphertext));
    }

    [Fact]
    public void DataProtection_PersistedKey_SurvivesHostRestart()
    {
        // Encrypt with the first host instance.
        string ciphertext;
        using(DpFactory factory = new DpFactory(_dir)) {
            ciphertext = factory.Services
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("Lantern.Test.Credential")
                .Protect("persisted-secret");
        }

        // Start a second host pointing at the same key directory — must decrypt.
        using(DpFactory factory = new DpFactory(_dir)) {
            string recovered = factory.Services
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("Lantern.Test.Credential")
                .Unprotect(ciphertext);
            Assert.Equal("persisted-secret", recovered);
        }
    }

    [Fact]
    public void DataProtection_PurposeIsolation_PreventsCrossRead()
    {
        using DpFactory factory = new DpFactory(_dir);
        IDataProtectionProvider provider = factory.Services
            .GetRequiredService<IDataProtectionProvider>();

        string ciphertext = provider.CreateProtector("Lantern.Purpose.A").Protect("secret");
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() =>
            provider.CreateProtector("Lantern.Purpose.B").Unprotect(ciphertext));
    }

    [Fact]
    public void DataProtection_KeyRingDirectory_ContainsXmlKeyFiles()
    {
        using DpFactory factory = new DpFactory(_dir);
        // Force key material to be written by using the protector.
        factory.Services
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Lantern.Test.Init")
            .Protect("warmup");

        string keyDir = Path.Combine(_dir, "keys");
        Assert.True(Directory.Exists(keyDir), "Data Protection key directory was not created.");
        Assert.NotEmpty(Directory.GetFiles(keyDir, "key-*.xml"));
    }

    [Fact]
    public void DataProtection_KeyRingXmlContent_IsNotAMeilisearchApiKey()
    {
        // This test documents the architecture invariant: key-ring XML files hold
        // encrypted AES key material for Data Protection, not application credentials.
        // Reading a key-ring file and passing its content to MeilisearchClient as a
        // Bearer token will always fail authentication.
        using DpFactory factory = new DpFactory(_dir);
        factory.Services
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Lantern.Test.Init")
            .Protect("warmup");

        string keyDir = Path.Combine(_dir, "keys");
        string xmlFile = Directory.GetFiles(keyDir, "key-*.xml").First();
        string content = File.ReadAllText(xmlFile).Trim();

        // Key-ring files are XML documents containing AES/HMAC descriptors.
        Assert.StartsWith("<?xml", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<key ", content);

        // A valid Meilisearch API key is a plain printable token, not multi-line XML.
        Assert.False(
            System.Text.RegularExpressions.Regex.IsMatch(content, @"^[\w\-]{16,}$"),
            "Data Protection key-ring XML must not resemble a valid Meilisearch API key token.");
    }

    public async ValueTask DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        await Task.Yield();
        if(Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    // ── Minimal factory ───────────────────────────────────────────────────────

    private sealed class DpFactory(string directory) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting("Storage:DatabasePath",
                Path.Combine(directory, "database", "lantern.db"));
            builder.UseSetting("Storage:DataProtectionKeysPath",
                Path.Combine(directory, "keys"));
            builder.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?> {
                    ["Meilisearch:Url"] = "http://127.0.0.1:1",
                    ["Meilisearch:IndexName"] = "dp-test-index",
                    ["MeilisearchProcess:Managed"] = "false",
                    ["DevAdmin:Username"] = "",
                    ["DevAdmin:Password"] = ""
                }));
        }
    }
}
