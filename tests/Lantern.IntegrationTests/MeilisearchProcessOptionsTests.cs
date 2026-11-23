using Lantern.Web;

namespace Lantern.IntegrationTests;

/// <summary>
/// Tests for <see cref="MeilisearchProcessOptions"/> defaults and network policy configuration.
/// </summary>
public sealed class MeilisearchProcessOptionsTests
{
    [Fact]
    public void AllowedIpNetworks_Default_IsLoopbackCidr()
    {
        // The default must be the loopback CIDR so that a managed Meilisearch can reach
        // the LlamaPure embedding server on 127.0.0.1 without additional configuration.
        // If this default is changed to empty, Meilisearch will reject the REST embedder
        // URL with "bad uri: Rejected URI" (SSRF protection introduced in Meilisearch 1.9+).
        MeilisearchProcessOptions opts = new MeilisearchProcessOptions();
        Assert.Equal("127.0.0.0/8", opts.AllowedIpNetworks);
    }

    [Fact]
    public void AllowedIpNetworks_CanBeCleared_ForExternalMeilisearch()
    {
        // External Meilisearch deployments are configured independently;
        // an empty string must suppress the env-var injection in MeilisearchProcessManager.
        MeilisearchProcessOptions opts = new MeilisearchProcessOptions { AllowedIpNetworks = "" };
        Assert.Equal("", opts.AllowedIpNetworks);
    }

    [Fact]
    public void AllowedIpNetworks_CanBeOverridden_WithCustomCidr()
    {
        MeilisearchProcessOptions opts = new MeilisearchProcessOptions { AllowedIpNetworks = "::1/128" };
        Assert.Equal("::1/128", opts.AllowedIpNetworks);
    }
}
