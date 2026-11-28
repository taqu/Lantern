using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lantern.Embedding.Tests;

public sealed class EmbeddingOptionsTests
{
    [Theory]
    [InlineData("ModelPath", " ")]
    [InlineData("Port", "65536")]
    [InlineData("ListenAddress", "ftp://localhost")]
    [InlineData("QueueCapacity", "0")]
    [InlineData("ContextSize", "0")]
    [InlineData("Threads", "0")]
    [InlineData("RequestTimeoutSeconds", "-1")]
    public void InvalidSettingsIdentifyConfigurationFailure(string key, string value)
    {
        using var provider = Services(key, value);
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<EmbeddingOptions>>().Value);
    }

    [Fact]
    public void DefaultEndpointUsesLoopback()
    {
        using var provider = Services("Port", "7701");
        var options = provider.GetRequiredService<IOptions<EmbeddingOptions>>().Value;
        Assert.Equal("127.0.0.1", options.ListenAddress);
        Assert.Equal(7701, options.Port);
    }

    private static ServiceProvider Services(string key, string value)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            [$"Embedding:{key}"] = value
        }).Build();
        return new ServiceCollection().AddLanternEmbedding(config).BuildServiceProvider();
    }
}
