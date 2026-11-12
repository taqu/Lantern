using Lantern.Infrastructure.Search;

namespace Lantern.IntegrationTests;

/// <summary>
/// Tests that <see cref="MeilisearchOptions.ResolveApiKey"/> reads credentials
/// from the configured file path and does not interpret other file types
/// (such as ASP.NET Core Data Protection key-ring XML) as valid API keys.
/// </summary>
public sealed class MeilisearchOptionsTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "meili-opts-test-" + Guid.NewGuid().ToString("N"));

    public MeilisearchOptionsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void ResolveApiKey_NoFileConfigured_ReturnsEmpty()
    {
        var opts = new MeilisearchOptions { ApiKeyFile = "" };
        Assert.Equal("", opts.ResolveApiKey());
    }

    [Fact]
    public void ResolveApiKey_FileExists_ReturnsTrimmedContent()
    {
        string file = Path.Combine(_dir, "meili-api-key.txt");
        File.WriteAllText(file, "  my-scoped-api-key  \n");
        var opts = new MeilisearchOptions { ApiKeyFile = file };
        Assert.Equal("my-scoped-api-key", opts.ResolveApiKey());
    }

    [Fact]
    public void ResolveApiKey_FileMissing_ReturnsEmpty()
    {
        var opts = new MeilisearchOptions { ApiKeyFile = Path.Combine(_dir, "nonexistent.txt") };
        // Env var is not set in this process (guard: clear it)
        string? saved = Environment.GetEnvironmentVariable("MEILI_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("MEILI_API_KEY", null);
            Assert.Equal("", opts.ResolveApiKey());
        }
        finally
        {
            Environment.SetEnvironmentVariable("MEILI_API_KEY", saved);
        }
    }

    [Fact]
    public void ResolveApiKey_FilePresent_TakesPrecedenceOverEnvVar()
    {
        string file = Path.Combine(_dir, "api-key.txt");
        File.WriteAllText(file, "file-key");
        var opts = new MeilisearchOptions { ApiKeyFile = file };
        string? saved = Environment.GetEnvironmentVariable("MEILI_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("MEILI_API_KEY", "env-key");
            Assert.Equal("file-key", opts.ResolveApiKey());
        }
        finally
        {
            Environment.SetEnvironmentVariable("MEILI_API_KEY", saved);
        }
    }

    [Fact]
    public void ResolveApiKey_DataProtectionXmlAsApiKeyFile_ReturnsXmlNotKey()
    {
        // Regression guard: documents the bug where ApiKeyFile was accidentally set to
        // an ASP.NET Core Data Protection key-ring XML path in appsettings.Development.json.
        //
        // ResolveApiKey() reads the file as plain text and returns whatever it contains.
        // If it contains XML, that XML is sent as the Bearer token and Meilisearch rejects it.
        // The fix is to never set ApiKeyFile to a key-ring XML file — only to meili-api-key.txt.
        const string keyRingXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <key id="b7cad4c6-6104-4618-8581-5ea18687c2b4" version="1">
              <descriptor deserializerType="Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel.AuthenticatedEncryptorDescriptorDeserializer, Microsoft.AspNetCore.DataProtection">
                <descriptor>
                  <encryption algorithm="AES_256_CBC" />
                  <validation algorithm="HMACSHA256" />
                  <encryptedSecret decryptorType="Microsoft.AspNetCore.DataProtection.XmlEncryption.DpapiXmlDecryptor, Microsoft.AspNetCore.DataProtection">
                    <encryptedKey xmlns=""><value>AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA</value></encryptedKey>
                  </encryptedSecret>
                </descriptor>
              </descriptor>
            </key>
            """;
        string xmlFile = Path.Combine(_dir, "key-b7cad4c6.xml");
        File.WriteAllText(xmlFile, keyRingXml);

        var opts = new MeilisearchOptions { ApiKeyFile = xmlFile };
        string result = opts.ResolveApiKey();

        // The result is XML, not a valid Meilisearch scoped API key
        Assert.StartsWith("<?xml", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DpapiXmlDecryptor", result);
        // A valid Meilisearch key looks like a plain alphanumeric/dash token, not XML
        Assert.False(System.Text.RegularExpressions.Regex.IsMatch(result, @"^[\w\-]{16,}$"),
            "Key-ring XML must not be mistaken for a valid Meilisearch API key.");
    }
}
