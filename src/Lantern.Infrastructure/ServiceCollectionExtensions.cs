using Lantern.Core.Administration;
using Lantern.Core.Crawling;
using Lantern.Core.Extraction;
using Lantern.Core.Jobs;
using Lantern.Core.Search;
using Lantern.Infrastructure.Extraction;
using Lantern.Infrastructure.Persistence;
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
        services.Configure<ChunkingOptions>(
            configuration.GetSection(ChunkingOptions.SectionName));
        services.Configure<HybridSearchOptions>(
            configuration.GetSection(HybridSearchOptions.SectionName));

        services.AddSingleton(sp => new Meilisearch.MeilisearchClient(
            sp.GetRequiredService<IOptions<MeilisearchOptions>>().Value.Url,
            Environment.GetEnvironmentVariable("MEILI_API_KEY") ?? ""));
        services.AddSingleton(sp => new MeilisearchIndexingService(
            sp.GetRequiredService<Meilisearch.MeilisearchClient>(),
            sp.GetRequiredService<IOptions<MeilisearchOptions>>().Value,
            sp.GetRequiredService<ILogger<MeilisearchIndexingService>>()));
        services.AddSingleton<IDocumentIndexingService>(sp => sp.GetRequiredService<MeilisearchIndexingService>());
        services.AddSingleton<ISearchEngineHealthService>(sp => new MeilisearchHealthService(
            sp.GetRequiredService<Meilisearch.MeilisearchClient>(),
            sp.GetRequiredService<IOptions<MeilisearchOptions>>().Value));

        services.AddSingleton<ISearchService>(sp =>
        {
            MeilisearchOptions options = sp.GetRequiredService<IOptions<MeilisearchOptions>>().Value;
            string apiKey = Environment.GetEnvironmentVariable("MEILI_API_KEY") ?? "";
            ILogger<MeilisearchSearchService> logger = sp.GetRequiredService<ILogger<MeilisearchSearchService>>();
            HybridSearchOptions hybridOptions = sp.GetRequiredService<IOptions<HybridSearchOptions>>().Value;
            return new MeilisearchSearchService(options, apiKey, logger, sp.GetRequiredService<IDocumentIndexingService>(), hybridOptions);
        });

        services.AddSingleton<ITextChunkingService, TextChunkingService>();
        services.AddSingleton<IChunkIndexingService, MeilisearchChunkIndexingService>();
        services.AddSingleton<EmbedderConfigurationService>();

        // Document extraction services
        services.Configure<ExtractionOptions>(configuration.GetSection(ExtractionOptions.SectionName));
        services.AddSingleton<DocumentFormatDetector>();
        services.AddSingleton<IDocumentExtractor, PlainTextExtractor>();
        services.AddSingleton<IDocumentExtractor, MarkdownExtractor>();
        services.AddSingleton<IDocumentExtractor, HtmlExtractor>();
        services.AddSingleton<IDocumentExtractor, PdfExtractor>();
        services.AddSingleton<IDocumentExtractor, DocxExtractor>();
        services.AddSingleton<IDocumentExtractor, XlsxExtractor>();
        services.AddSingleton<IDocumentExtractor, PptxExtractor>();
        services.AddSingleton<IDocumentExtractor, GeneralTextExtractor>();
        services.AddSingleton<EncodingDetector>();
        services.AddSingleton<DocumentExtractionDispatcher>();
        services.AddSingleton<StructureAwareChunker>();
        services.AddSingleton<IDocumentProcessingService, DocumentProcessingService>();

        services.AddSingleton<ICrawlStateRepository, CrawlStateRepository>();
        services.AddSingleton<ICrawlerJobRepository, CrawlerJobRepository>();
        services.AddSingleton<ICrawlerScheduleRepository, CrawlerScheduleRepository>();
        services.AddSingleton<ICrawledUrlStateRepository, CrawledUrlStateRepository>();
        services.AddSingleton<ISearchCategoryRepository, SearchCategoryRepository>();
        services.AddSingleton<ICrawlerCategoryResolver, CrawlerCategoryResolver>();

        return services;
    }
}
