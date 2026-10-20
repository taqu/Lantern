using System.Threading.RateLimiting;
using Lantern.Core.Administration;
using Lantern.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Lantern.Web.Administration;

public static class AdministrationSetup
{
    public static void AddLanternAdministration(this WebApplicationBuilder builder)
    {
        string root = builder.Environment.IsDevelopment()
            ? Path.Combine(builder.Environment.ContentRootPath, "App_Data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Lantern");
        string dbPath = Path.GetFullPath(builder.Configuration["Storage:DatabasePath"] ?? Path.Combine(root, "database", "lantern.db"), builder.Environment.ContentRootPath);
        string keyPath = Path.GetFullPath(builder.Configuration["Storage:DataProtectionKeysPath"] ?? Path.Combine(root, "keys"), builder.Environment.ContentRootPath);
        string webRoot = Path.GetFullPath(builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"));
        foreach (string? path in new[] { dbPath, keyPath })
            if (path.Equals(webRoot, StringComparison.OrdinalIgnoreCase) || path.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Database and authentication keys must be stored outside the web root.");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        Directory.CreateDirectory(keyPath);
        var connection = new SqliteConnectionStringBuilder { DataSource = dbPath, ForeignKeys = true };
        builder.Services.AddDbContextFactory<LanternDbContext>(options => options.UseSqlite(connection.ToString()));
        builder.Services.AddSingleton<IApplicationSettingsService, ApplicationSettingsService>();
        builder.Services.AddSingleton<AdministratorAuthentication>();
        builder.Services.AddSingleton<PathTestService>();
        builder.Services.AddScoped<AdministratorCookieEvents>();
        builder.Services.AddScoped<AdministrationService>();
        builder.Services.AddScoped<CrawlerManagementService>();
        builder.Services.AddScoped<CategoryManagementService>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddCascadingAuthenticationState();
        IDataProtectionBuilder protection = builder.Services.AddDataProtection().SetApplicationName("Lantern").PersistKeysToFileSystem(new DirectoryInfo(keyPath));
        if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.LoginPath = "/account/login";
            options.AccessDeniedPath = "/account/denied";
            options.Cookie.Name = "__Host-Lantern.Admin";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
            options.SlidingExpiration = false;
            options.EventsType = typeof(AdministratorCookieEvents);
        });
        builder.Services.AddAuthorization(options => options.AddPolicy(AdministratorAuthentication.Policy,
            policy => policy.RequireAuthenticatedUser().RequireRole(AdministratorAuthentication.Policy)));
        builder.Services.AddAntiforgery(options =>
        {
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
        builder.Services.Configure<FormOptions>(options =>
        {
            options.ValueLengthLimit = 1024;
            options.ValueCountLimit = 16;
        });
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
    }

    public static async Task InitializeAdministrationAsync(this WebApplication app)
    {
        try
        {
            IDbContextFactory<LanternDbContext> factory = app.Services.GetRequiredService<IDbContextFactory<LanternDbContext>>();
            await using LanternDbContext db = await factory.CreateDbContextAsync();
            await db.Database.MigrateAsync();
            await app.Services.GetRequiredService<AdministratorAuthentication>().ProvisionAsync(
                Environment.GetEnvironmentVariable("LANTERN_ADMIN_USERNAME"), Environment.GetEnvironmentVariable("LANTERN_ADMIN_PASSWORD"));
        }
        catch (Exception ex)
        {
            app.Logger.LogCritical("Administration initialization failed. Check the database directory permissions, migrations, and initial administrator provisioning.");
            throw new InvalidOperationException("Could not initialize Lantern administration. Existing data was preserved; check server configuration and database access.", ex);
        }
    }
}
