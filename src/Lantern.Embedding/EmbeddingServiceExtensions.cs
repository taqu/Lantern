using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lantern.Embedding;

public static class EmbeddingServiceExtensions
{
    public static IServiceCollection AddLanternEmbedding(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EmbeddingOptions>(configuration.GetSection(EmbeddingOptions.SectionName));
        services.AddSingleton<EmbeddingServiceStatus>();
        services.AddSingleton<InferenceQueue>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<EmbeddingOptions>>().Value;
            return new InferenceQueue(options.QueueCapacity);
        });
        services.AddHostedService<InferenceWorker>();
        services.AddHostedService<EmbeddingServer>();
        return services;
    }
}
