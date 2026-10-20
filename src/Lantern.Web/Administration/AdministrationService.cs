using Lantern.Core.Administration;
using Lantern.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lantern.Web.Administration;

// Admin components are static SSR: authorization is checked against the current HTTP request.
public sealed class AdministrationService(
    IHttpContextAccessor accessor, IAuthorizationService authorization,
    IApplicationSettingsService settings, ISearchEngineHealthService health,
    IDbContextFactory<LanternDbContext> factory)
{
    private async Task DemandAsync()
    {
        System.Security.Claims.ClaimsPrincipal? user = accessor.HttpContext?.User;
        if (user == null || !(await authorization.AuthorizeAsync(user, null, AdministratorAuthentication.Policy)).Succeeded)
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
}
