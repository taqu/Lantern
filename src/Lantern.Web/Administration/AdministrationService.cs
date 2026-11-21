using Lantern.Core.Administration;
using Lantern.Embedding;
using Lantern.Infrastructure.Persistence;
using Lantern.Infrastructure.Search;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Lantern.Web.Administration;

// Admin components are static SSR: authorization is checked against the current HTTP request.
public sealed class AdministrationService(
    IHttpContextAccessor accessor, IAuthorizationService authorization,
    IApplicationSettingsService settings, ISearchEngineHealthService health,
    IDbContextFactory<LanternDbContext> factory, IServiceProvider sp)
{
    private async Task DemandAsync()
    {
        System.Security.Claims.ClaimsPrincipal? user = accessor.HttpContext?.User;
        if(user == null || !(await authorization.AuthorizeAsync(user, null, AdministratorAuthentication.Policy)).Succeeded)
            throw new UnauthorizedAccessException("Administrator authorization is required.");
    }

    public async Task<ApplicationSetting> GetSettingsAsync(CancellationToken ct = default)
    { await DemandAsync(); return await settings.GetAsync(ct); }

    public async Task SaveSettingsAsync(ApplicationSetting value, CancellationToken ct = default)
    { await DemandAsync(); await settings.SaveAsync(value, ct); }

    public async Task<SearchEngineStatus> GetHealthAsync(bool refresh = false, CancellationToken ct = default)
    { await DemandAsync(); return await health.GetAsync(refresh, ct); }

    public async Task<bool> DatabaseAvailableAsync(CancellationToken ct = default)
    {
        await DemandAsync();
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        return await db.Database.CanConnectAsync(ct);
    }

    public async Task<List<CrawlerDefinition>> GetCrawlersAsync(CancellationToken ct = default)
    {
        await DemandAsync();
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        return await db.CrawlerDefinitions.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
    }

    public async Task<EmbeddingHealthStatus> GetEmbeddingHealthAsync(CancellationToken ct = default)
    {
        await DemandAsync();

        IConfiguration config = sp.GetRequiredService<IConfiguration>();
        bool embeddingEnabled = config.GetValue<bool>("Embedding:Enabled");
        bool hybridEnabled = config.GetValue<bool>("HybridSearch:Enabled");
        string chunkIndexName = config.GetValue<string>("HybridSearch:ChunkIndexName") ?? "lantern-chunks";

        string modelStatus = embeddingEnabled ? "Service not started" : "Disabled";
        if(embeddingEnabled) {
            EmbeddingServiceStatus? embeddingStatus = sp.GetService<EmbeddingServiceStatus>();
            modelStatus = embeddingStatus != null ? embeddingStatus.Status.ToString() : "Service not registered";
        }

        long? chunkCount = null;
        if(hybridEnabled) {
            try {
                IMeilisearchClientFactory clientFactory = sp.GetRequiredService<IMeilisearchClientFactory>();
                using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                var client = clientFactory.GetIndexingClient();
                var stats = await client.Index(chunkIndexName).GetStatsAsync(deadline.Token);
                chunkCount = stats.NumberOfDocuments;
            } catch { }
        }

        return new EmbeddingHealthStatus(embeddingEnabled, modelStatus, hybridEnabled, chunkIndexName, chunkCount);
    }
}
