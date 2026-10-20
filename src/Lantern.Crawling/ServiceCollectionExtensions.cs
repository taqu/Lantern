using Lantern.Core.Crawling;
using Lantern.Crawling.Files;
using Lantern.Crawling.Jobs;
using Lantern.Crawling.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lantern.Crawling;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLanternCrawling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<FileCrawlingOptions>(configuration.GetSection(FileCrawlingOptions.SectionName));
        services.Configure<JobOptions>(configuration.GetSection(JobOptions.SectionName));
        services.AddSingleton<DirectoryEnumerator>();
        services.AddSingleton<FileSynchronizationService>();
        services.AddSingleton<DeletionReconciler>();
        services.AddSingleton<IFileCrawler, FileCrawler>();
        services.Configure<WebCrawlingOptions>(configuration.GetSection(WebCrawlingOptions.SectionName));
        services.AddSingleton<CrawlBoundaryValidator>();
        services.AddSingleton<IWebCrawler, WebCrawler>();
        services.AddSingleton<CrawlerJobService>();
        services.AddSingleton<JobDispatcherService>();
        services.AddHostedService(sp => sp.GetRequiredService<JobDispatcherService>());
        services.AddHostedService<CrawlerSchedulerService>();
        services.AddHostedService<RetentionCleanupService>();
        return services;
    }
}
