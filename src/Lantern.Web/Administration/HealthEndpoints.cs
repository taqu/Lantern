using Lantern.Core.Administration;
using Lantern.Core.Jobs;
using Lantern.Core.Search;
using Lantern.Embedding;
using Lantern.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lantern.Web.Administration;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        // Public health endpoint — returns application and component status.
        // Does not expose internal configuration or secrets.
        app.MapGet("/api/health", async (
            ISearchEngineHealthService meiliHealth,
            IDbContextFactory<LanternDbContext> dbFactory,
            ISchedulerHealthService schedulerHealth,
            [Microsoft.AspNetCore.Mvc.FromServices] MeilisearchProcessManager? processManager,
            [Microsoft.AspNetCore.Mvc.FromServices] EmbeddingServiceStatus? embeddingStatus,
            [Microsoft.AspNetCore.Mvc.FromServices] HybridSearchReadiness? hybridReadiness,
            CancellationToken ct) => {
                string version = typeof(HealthEndpoints).Assembly.GetName().Version?.ToString(3) ?? "unknown";

                // SQLite connectivity
                string sqliteStatus;
                try {
                    await using LanternDbContext db = await dbFactory.CreateDbContextAsync(ct);
                    sqliteStatus = await db.Database.CanConnectAsync(ct) ? "ready" : "error";
                } catch {
                    sqliteStatus = "error";
                }

                // Meilisearch
                string meiliStatus;
                try {
                    SearchEngineStatus s = await meiliHealth.GetAsync(refresh: false, ct);
                    meiliStatus = s.Available ? "ready" : "error";
                } catch {
                    meiliStatus = "error";
                }

                string? meiliProcess = processManager == null ? null
                    : processManager.IsRunning ? "running" : "stopped";

                SchedulerStatus schedulerStatus = schedulerHealth.GetStatus();
                string schedulerState = schedulerStatus.State.ToString().ToLowerInvariant();

                string? embeddingState = embeddingStatus == null ? null
                    : embeddingStatus.Status.ToString().ToLowerInvariant();
                bool? hybridReady = hybridReadiness?.IsReady;

                bool healthy = sqliteStatus == "ready" && meiliStatus == "ready" && schedulerStatus.State != SchedulerState.Faulted;
                string overall = healthy ? "healthy" : sqliteStatus == "ready" ? "degraded" : "unhealthy";

                return Results.Ok(new {
                    status = overall,
                    version,
                    components = new {
                        database = sqliteStatus,
                        meilisearch = meiliStatus,
                        meilisearchProcess = meiliProcess,
                        scheduler = schedulerState,
                        schedulerError = schedulerStatus.LastError,
                        embedding = embeddingState,
                        hybridSearch = hybridReady,
                    },
                    timestamp = DateTimeOffset.UtcNow,
                });
            }).AllowAnonymous();
    }
}
