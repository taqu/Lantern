using Lantern.Crawling;
using Lantern.Embedding;
using Lantern.Infrastructure;
using Lantern.Web;
using Lantern.Web.Administration;
using Lantern.Web.Components;

// When LANTERN_DEV_RESET_ADMIN=1 the process resets the admin password and exits
// without starting the web server. Only permitted in Development.
bool isDevReset = string.Equals(Environment.GetEnvironmentVariable("LANTERN_DEV_RESET_ADMIN"), "1", StringComparison.Ordinal);

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
if (isDevReset) builder.Logging.ClearProviders();

// Windows Service support — enables running under the Service Control Manager.
// Has no effect on non-Windows platforms or when running interactively.
if (OperatingSystem.IsWindows())
{
    builder.Host.UseWindowsService(options => options.ServiceName = "Lantern");
}

if (builder.Environment.IsDevelopment())
{
    // Load machine-local development overrides (absolute paths written by setup-dev.ps1).
    // This file is gitignored (matches appsettings.*.Local.json pattern).
    string devLocalConfig = Path.Combine(builder.Environment.ContentRootPath, "appsettings.Development.Local.json");
    builder.Configuration.AddJsonFile(devLocalConfig, optional: true, reloadOnChange: false);
}
else
{
    // Load persistent production configuration from the shared data directory.
    // This file is created by Initialize-Lantern.ps1 and survives application upgrades.
    string productionConfig = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Lantern", "config", "appsettings.json");
    if (File.Exists(productionConfig))
    {
        builder.Configuration.AddJsonFile(productionConfig, optional: true, reloadOnChange: false);
    }
}

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddLanternInfrastructure(builder.Configuration);
builder.Services.AddLanternCrawling(builder.Configuration);
builder.AddLanternAdministration();

// Managed Meilisearch process (optional — disabled by default).
MeilisearchProcessOptions meiliProcessOpts =
    builder.Configuration.GetSection(MeilisearchProcessOptions.SectionName)
    .Get<MeilisearchProcessOptions>() ?? new();
if (meiliProcessOpts.Managed)
{
    builder.Services.Configure<MeilisearchProcessOptions>(
        builder.Configuration.GetSection(MeilisearchProcessOptions.SectionName));
    builder.Services.AddSingleton<MeilisearchProcessManager>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<MeilisearchProcessManager>());
}

EmbeddingOptions embeddingOptions =
    builder.Configuration.GetSection(EmbeddingOptions.SectionName).Get<EmbeddingOptions>() ?? new();
if (embeddingOptions.Enabled)
{
    builder.Services.AddLanternEmbedding(builder.Configuration);
}

WebApplication app = builder.Build();

if (isDevReset)
{
    if (!app.Environment.IsDevelopment())
    {
        await Console.Error.WriteLineAsync("Error: LANTERN_DEV_RESET_ADMIN is only permitted in the Development environment.");
        Environment.Exit(1);
        return;
    }
    await app.InitializeAdministrationAsync();
    byte[] pwBytes = new byte[15];
    System.Security.Cryptography.RandomNumberGenerator.Fill(pwBytes);
    string newPassword = Convert.ToBase64String(pwBytes).TrimEnd('=');
    await app.Services.GetRequiredService<AdministratorAuthentication>().ResetPasswordAsync(newPassword);
    Console.WriteLine(newPassword);
    return;
}

await app.InitializeAdministrationAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["X-Permitted-Cross-Domain-Policies"] = "none";
    await next();
});
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();
app.MapHealthEndpoints();
app.MapAdministrationEndpoints();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

public partial class Program;
