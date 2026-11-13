using Lantern.Crawling;
using Lantern.Embedding;
using Lantern.Infrastructure;
using Lantern.Web;
using Lantern.Web.Administration;
using Lantern.Web.Components;
using Lantern.Web.Setup;

// ── Subcommand detection ──────────────────────────────────────────────────────
// First positional arg that doesn't start with '--' is treated as a subcommand.
string? subcommand = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal)
    ? args[0].ToLowerInvariant()
    : null;

// Args forwarded to WebApplicationBuilder (strip the subcommand token).
string[] webArgs = subcommand != null ? args.Skip(1).ToArray() : args;

// ── Legacy one-shot reset modes (backward-compatible) ────────────────────────
// LANTERN_DEV_RESET_ADMIN=1 : resets admin password in Development and exits.
bool isDevReset = string.Equals(Environment.GetEnvironmentVariable("LANTERN_DEV_RESET_ADMIN"), "1", StringComparison.Ordinal);

// --admin-reset : resets password via LANTERN_ADMIN_RESET_PASSWORD env var and exits.
bool isAdminReset = args.Contains("--admin-reset");

// ── Stateless commands (no DI needed) ────────────────────────────────────────
if(subcommand == "status") {
    int code = await LanternSetup.RunStatusAsync();
    return code;
}

if(args.Contains("--help") || args.Contains("-h")) {
    PrintHelp();
    return 0;
}

// ── Setup Phase 1 (pre-host): directories, secrets, config file ──────────────
string? setupDataRoot = null;
if(subcommand == "setup") {
    setupDataRoot = await LanternSetup.RunPhase1Async(webArgs);
    // Fall through — web host is needed for Phase 2 (DB migrations, admin).
    // Don't forward setup-specific flags (--data-dir, --show-master-key) to the web builder.
    webArgs = webArgs.Where(a => a != "--show-master-key" && a != "--data-dir" && !a.StartsWith("--data-dir=")).ToArray();
    // Strip the value after --data-dir
    List<string> filtered = new List<string>();
    for(int i = 0; i < webArgs.Length; i++) {
        if(webArgs[i] == "--data-dir") { i++; continue; }
        filtered.Add(webArgs[i]);
    }
    webArgs = [.. filtered];
}

// ── Build the web application host ───────────────────────────────────────────
WebApplicationBuilder builder = WebApplication.CreateBuilder(isDevReset || isAdminReset || subcommand != null
    ? webArgs
    : args);

if(isDevReset || isAdminReset || subcommand is "setup" or "admin" or "meilisearch")
    builder.Logging.ClearProviders();

// Windows Service support — has no effect on non-Windows or interactive runs.
if(OperatingSystem.IsWindows())
    builder.Host.UseWindowsService(options => options.ServiceName = "Lantern");

// systemd support on Linux — signals readiness and handles SIGTERM cleanly.
if(OperatingSystem.IsLinux())
    builder.Host.UseSystemd();

// Configuration loading.
if(builder.Environment.IsDevelopment()) {
    // Machine-local development overrides (absolute paths written by setup-dev scripts).
    // This file is gitignored (matches appsettings.*.Local.json).
    string devLocalConfig = Path.Combine(builder.Environment.ContentRootPath, "appsettings.Development.Local.json");
    builder.Configuration.AddJsonFile(devLocalConfig, optional: true, reloadOnChange: false);
} else {
    // Persistent production configuration. Created by 'Lantern.Web setup' and survives upgrades.
    if(File.Exists(PlatformDirectories.ProductionConfigFile))
        builder.Configuration.AddJsonFile(PlatformDirectories.ProductionConfigFile, optional: true, reloadOnChange: false);
}

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddLanternInfrastructure(builder.Configuration);
builder.Services.AddLanternCrawling(builder.Configuration);
builder.AddLanternAdministration();

// MeilisearchProcessOptions is bound unconditionally so IMasterKeyAccessor
// can locate the master key file whether or not Lantern owns the process.
builder.Services.Configure<MeilisearchProcessOptions>(
    builder.Configuration.GetSection(MeilisearchProcessOptions.SectionName));
builder.Services.AddSingleton<Lantern.Infrastructure.Search.IMasterKeyAccessor, MeilisearchMasterKeyAccessor>();

// Managed Meilisearch process and embedding are not needed in one-shot command modes.
bool isCommandMode = isDevReset || isAdminReset || subcommand != null;
if(!isCommandMode) {
    MeilisearchProcessOptions meiliProcessOpts =
        builder.Configuration.GetSection(MeilisearchProcessOptions.SectionName)
        .Get<MeilisearchProcessOptions>() ?? new();
    if(meiliProcessOpts.Managed) {
        builder.Services.AddSingleton<MeilisearchProcessManager>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<MeilisearchProcessManager>());
    }

    // Must be registered AFTER the process manager so the managed server is
    // live before EnsureCredentialsAsync is called.
    builder.Services.AddHostedService<Lantern.Infrastructure.Search.MeilisearchCredentialBootstrapper>();

    EmbeddingOptions embeddingOptions =
        builder.Configuration.GetSection(EmbeddingOptions.SectionName).Get<EmbeddingOptions>() ?? new();
    if(embeddingOptions.Enabled)
        builder.Services.AddLanternEmbedding(builder.Configuration);
}

WebApplication app = builder.Build();

// ── Legacy dev reset mode ─────────────────────────────────────────────────────
if(isDevReset) {
    if(!app.Environment.IsDevelopment()) {
        await Console.Error.WriteLineAsync("Error: LANTERN_DEV_RESET_ADMIN is only permitted in the Development environment.");
        Environment.Exit(1);
        return 1;
    }
    await app.InitializeAdministrationAsync();
    byte[] pwBytes = new byte[15];
    System.Security.Cryptography.RandomNumberGenerator.Fill(pwBytes);
    string newPassword = Convert.ToBase64String(pwBytes).TrimEnd('=');
    await app.Services.GetRequiredService<AdministratorAuthentication>().ResetPasswordAsync(newPassword);
    Console.WriteLine(newPassword);
    return 0;
}

// ── Legacy --admin-reset mode ─────────────────────────────────────────────────
if(isAdminReset) {
    string? resetPassword = Environment.GetEnvironmentVariable("LANTERN_ADMIN_RESET_PASSWORD");
    Environment.SetEnvironmentVariable("LANTERN_ADMIN_RESET_PASSWORD", null);
    if(string.IsNullOrEmpty(resetPassword)) {
        await Console.Error.WriteLineAsync("Error: --admin-reset requires LANTERN_ADMIN_RESET_PASSWORD environment variable.");
        await Console.Error.WriteLineAsync("       Use reset-admin.ps1 / reset-admin.sh or 'Lantern.Web admin reset-password'.");
        Environment.Exit(1);
        return 1;
    }
    await app.InitializeAdministrationAsync();
    try {
        await app.Services.GetRequiredService<AdministratorAuthentication>().ResetPasswordAsync(resetPassword);
        Console.WriteLine("Administrator password reset successfully. Existing sessions have been invalidated.");
    } catch(InvalidOperationException ex) {
        await Console.Error.WriteLineAsync($"Error: {ex.Message}");
        Environment.Exit(1);
        return 1;
    }
    return 0;
}

// ── Setup Phase 2 (post-host): migrations and admin provisioning ──────────────
if(subcommand == "setup") {
    await app.InitializeAdministrationAsync();
    await LanternSetup.RunPhase2Async(app, webArgs);
    Console.WriteLine();
    Console.WriteLine("Setup complete.");
    Console.WriteLine($"  Config : {PlatformDirectories.ProductionConfigFile}");
    Console.WriteLine($"  Data   : {setupDataRoot ?? PlatformDirectories.DefaultDataRoot}");
    Console.WriteLine();
    Console.WriteLine("Start Lantern with:  ./start-lantern.sh  (Linux) or  .\\start-lantern.ps1  (Windows)");
    return 0;
}

// ── Interactive admin password reset ─────────────────────────────────────────
if(subcommand == "admin") {
    string adminVerb = webArgs.Length > 0 ? webArgs[0].ToLowerInvariant() : "";
    if(adminVerb != "reset-password") {
        await Console.Error.WriteLineAsync($"Unknown admin command: {adminVerb}");
        await Console.Error.WriteLineAsync("Available: admin reset-password");
        return 1;
    }
    await app.InitializeAdministrationAsync();
    return await LanternSetup.RunAdminResetPasswordAsync(app);
}

// ── Meilisearch subcommands ───────────────────────────────────────────────────
if(subcommand == "meilisearch") {
    string meiliVerb = webArgs.Length > 0 ? webArgs[0].ToLowerInvariant() : "";
    if(meiliVerb != "rotate-key") {
        await Console.Error.WriteLineAsync($"Unknown meilisearch command: {meiliVerb}");
        await Console.Error.WriteLineAsync("Available: meilisearch rotate-key");
        return 1;
    }
    // Key rotation is an operational command — not yet implemented.
    await Console.Error.WriteLineAsync("'meilisearch rotate-key' is not yet implemented.");
    await Console.Error.WriteLineAsync("To rotate manually: stop Lantern, replace meili-master-key.txt, delete meili-api-key.txt, restart.");
    return 1;
}

// ── Normal web application startup ───────────────────────────────────────────
await app.InitializeAdministrationAsync();

if(!app.Environment.IsDevelopment()) {
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.Use(async (context, next) => {
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
return 0;

// ── Helpers ───────────────────────────────────────────────────────────────────

static void PrintHelp()
{
    Console.WriteLine("""
        Lantern.Web — Lantern Search Service

        Usage:
          Lantern.Web                              Start the web server
          Lantern.Web setup [options]              Initialize or validate the installation
          Lantern.Web status                       Report health of Lantern and dependencies
          Lantern.Web admin reset-password         Reset the administrator password
          Lantern.Web --help                       Show this help

        Setup options:
          --data-dir <path>                        Override the persistent data directory
          --show-master-key                        Display the generated Meilisearch master key

        Environment variables (for non-interactive setup):
          LANTERN_ADMIN_USERNAME                   Initial administrator username
          LANTERN_ADMIN_PASSWORD                   Initial administrator password

        Default data directories:
          Windows  : %ProgramData%\Lantern
          Linux    : /var/lib/lantern  (data)  /etc/lantern  (config/secrets)
        """);
}

public partial class Program;
