using Lantern.Core.Search;
using Lantern.Infrastructure.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lantern.Infrastructure;

/// <summary>
/// Extension methods for registering Lantern infrastructure services.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLanternInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MeilisearchOptions>(
            configuration.GetSection(MeilisearchOptions.SectionName));

        services.AddSingleton<ISearchService>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MeilisearchOptions>>().Value;
            var apiKey = Environment.GetEnvironmentVariable("MEILI_API_KEY") ?? "";
            var logger = sp.GetRequiredService<ILogger<MeilisearchSearchService>>();
            return new MeilisearchSearchService(options, apiKey, logger);
        });

        return services;
    }
}
