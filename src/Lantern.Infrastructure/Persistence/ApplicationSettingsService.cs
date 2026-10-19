using Lantern.Core.Administration;
using Microsoft.EntityFrameworkCore;

namespace Lantern.Infrastructure.Persistence;

public sealed class ApplicationSettingsService(IDbContextFactory<LanternDbContext> factory) : IApplicationSettingsService
{
    public async Task<ApplicationSetting> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.ApplicationSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? new ApplicationSetting();
    }

    public async Task SaveAsync(ApplicationSetting settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var current = await db.ApplicationSettings.SingleOrDefaultAsync(cancellationToken);
        if (current == null) db.ApplicationSettings.Add(new ApplicationSetting { DisplayName = settings.DisplayName });
        else current.DisplayName = settings.DisplayName;
        await db.SaveChangesAsync(cancellationToken);
    }
}
