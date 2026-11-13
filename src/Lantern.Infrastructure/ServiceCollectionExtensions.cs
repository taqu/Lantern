using Lantern.Core.Administration;
using Lantern.Core.Crawling;
using Lantern.Core.Extraction;
using Lantern.Core.Jobs;
using Lantern.Core.Search;
using Lantern.Infrastructure.Extraction;
using Lantern.Infrastructure.Persistence;
using Lantern.Infrastructure.Search;
using Lantern.Infrastructure.Secrets;
using Meilisearch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        services.Configure<SecretStoreOptions>(
            configuration.GetSection(SecretStoreOptions.SectionName));

        services.AddSingleton<ISecretStore, FileSecretStore>();
        services.AddSingleton<IMeilisearchCredentialManager>(sp => new MeilisearchCredentialManager(
            sp.GetRequiredService<IOptions<MeilisearchOptions>>().Value,
            sp.GetRequiredService<IMasterKeyAccessor>(),
            sp.GetRequiredService<ISecretStore>(),
            sp.GetRequiredService<ILogger<MeilisearchCredentialManager>>()));
        services.AddSingleton<IMeilisearchClientFactory>(sp => new MeilisearchClientFactory(
            sp.GetRequiredService<IOptions<MeilisearchOptions>>().Value,
            sp.GetRequiredService<IMeilisearchCredentialManager>()));
        // NOTE: MeilisearchCredentialBootstrapper is registered by the host
        // (Program.cs) *after* the Meilisearch process manager so that the
        // managed server is already accepting requests before provisioning.

        // Legacy singleton: back it with the Indexing role so existing consumers
        // get a writable client without each being rewired individually. New code
        // should take IMeilisearchClientFactory directly.
        services.AddSingleton(sp =>
            sp.GetRequiredService<IMeilisearchClientFactory>().GetIndexingClient());

        services.AddSingleton(sp => new MeilisearchIndexingService(
            sp.GetRequiredService<IMeilisearchClientFactory>(),
            sp.GetRequiredService<IOptions<MeilisearchOptions>>().Value,
            sp.GetRequiredService<ILogger<MeilisearchIndexingService>>()));
        services.AddSingleton<IDocumentIndexingService>(sp => sp.GetRequiredService<MeilisearchIndexingService>());
        services.AddSingleton<ISearchEngineHealthService>(sp => new MeilisearchHealthService(
            sp.GetRequiredService<IMeilisearchClientFactory>().GetSearchClient(),
            sp.GetRequiredService<IOptions<MeilisearchOptions>>().Value));

        services.AddSingleton<ISearchService>(sp =>
        {
            MeilisearchOptions options = sp.GetRequiredService<IOptions<MeilisearchOptions>>().Value;
            MeilisearchClient client = sp.GetRequiredService<IMeilisearchClientFactory>().GetSearchClient();
            ILogger<MeilisearchSearchService> logger = sp.GetRequiredService<ILogger<MeilisearchSearchService>>();
            HybridSearchOptions hybridOptions = sp.GetRequiredService<IOptions<HybridSearchOptions>>().Value;
            return new MeilisearchSearchService(client, options, logger, sp.GetRequiredService<IDocumentIndexingService>(), hybridOptions);
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
        services.AddSingleton<IDocumentExtractor, DocExtractor>();
        services.AddSingleton<IDocumentExtractor, XlsxExtractor>();
        services.AddSingleton<IDocumentExtractor, PptxExtractor>();
        services.AddSingleton<IDocumentExtractor, PptExtractor>();
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
