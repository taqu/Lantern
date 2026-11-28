using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lantern.Embedding;

public static class EmbeddingServiceExtensions
{
    public static IServiceCollection AddLanternEmbedding(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmbeddingOptions>()
            .Bind(configuration.GetSection(EmbeddingOptions.SectionName))
            .Validate(o => o.GetValidationError() == null, "Invalid embedding configuration");
        services.AddSingleton<EmbeddingServiceStatus>();
        services.AddSingleton<InferenceQueue>(sp => {
            EmbeddingOptions options = sp.GetRequiredService<IOptions<EmbeddingOptions>>().Value;
            return new InferenceQueue(options.QueueCapacity);
        });
        services.AddHostedService<InferenceWorker>();
        services.AddHostedService<EmbeddingServer>();
        return services;
    }
}
