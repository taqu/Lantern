using Lantern.Core.Administration;
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
            MeilisearchProcessManager? processManager,
            CancellationToken ct) =>
        {
            string version = typeof(HealthEndpoints).Assembly.GetName().Version?.ToString(3) ?? "unknown";

            // SQLite connectivity
            string sqliteStatus;
            try
            {
                await using LanternDbContext db = await dbFactory.CreateDbContextAsync(ct);
                sqliteStatus = await db.Database.CanConnectAsync(ct) ? "ready" : "error";
            }
            catch
            {
                sqliteStatus = "error";
            }

            // Meilisearch
            string meiliStatus;
            try
            {
                SearchEngineStatus s = await meiliHealth.GetAsync(refresh: false, ct);
                meiliStatus = s.Available ? "ready" : "error";
            }
            catch
            {
                meiliStatus = "error";
            }

            string? meiliProcess = processManager == null ? null
                : processManager.IsRunning ? "running" : "stopped";

            bool healthy = sqliteStatus == "ready" && meiliStatus == "ready";
            string overall = healthy ? "healthy" : sqliteStatus == "ready" ? "degraded" : "unhealthy";

            return Results.Ok(new
            {
                status = overall,
                version,
                components = new
                {
                    database = sqliteStatus,
                    meilisearch = meiliStatus,
                    meilisearchProcess = meiliProcess,
                },
                timestamp = DateTimeOffset.UtcNow,
            });
        }).AllowAnonymous();
    }
}
