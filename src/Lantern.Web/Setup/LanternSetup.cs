using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lantern.Infrastructure.Persistence;
using Lantern.Web.Administration;
using Microsoft.EntityFrameworkCore;

namespace Lantern.Web.Setup;

/// <summary>
/// Platform-independent setup routine invoked by <c>Lantern.Web setup</c>.
///
/// Phase 1 (no DI): creates directories, generates secrets, sets permissions,
///                   writes the production configuration file.
/// Phase 2 (DI available): runs database migrations and provisions the first
///                          administrator account.
/// </summary>
public static class LanternSetup
{
    // ── Entry point ───────────────────────────────────────────────────────────

    /// <summary>
    /// Runs Phase 1 of setup. Called before the web application host is built.
    /// Returns the resolved data root so the caller can pass it to Phase 2.
    /// </summary>
    public static async Task<string> RunPhase1Async(string[] args, CancellationToken ct = default)
    {
        string dataRoot = GetArg(args, "--data-dir") ?? PlatformDirectories.DefaultDataRoot;
        bool showMasterKey = args.Contains("--show-master-key");

        string secretsDir = OperatingSystem.IsWindows()
            ? Path.Combine(dataRoot, "secrets")
            : "/etc/lantern/secrets";

        Console.WriteLine($"Lantern setup — data root: {dataRoot}");

        // Create persistent directories.
        await EnsureDirectoriesAsync(dataRoot, secretsDir);

        // Generate Meilisearch master key if absent.
        string masterKeyFile = Path.Combine(secretsDir, "meili-master-key.txt");
        string apiKeyFile = Path.Combine(secretsDir, "meili-api-key.txt");
        string masterKey = await EnsureMasterKeyAsync(masterKeyFile, ct);
        if (showMasterKey)
            Console.WriteLine($"Master key: {masterKey}");

        // Apply OS-level permissions to the secrets directory.
        ApplySecretPermissions(secretsDir);

        // Write the production configuration file from computed defaults.
        string configFile = PlatformDirectories.ProductionConfigFile;
        await EnsureProductionConfigAsync(configFile, dataRoot, secretsDir, masterKeyFile, apiKeyFile, ct);

        return dataRoot;
    }

    /// <summary>
    /// Runs Phase 2 of setup. Called after the web application host is built.
    /// Runs EF Core migrations and provisions the initial administrator.
    /// </summary>
    public static async Task RunPhase2Async(WebApplication app, string[] args, CancellationToken ct = default)
    {
        // DB migrations are run inside InitializeAdministrationAsync (called by caller).
        // Provision admin via env vars if present; otherwise prompt interactively.
        string? username = Environment.GetEnvironmentVariable("LANTERN_ADMIN_USERNAME");
        string? password = Environment.GetEnvironmentVariable("LANTERN_ADMIN_RESET_PASSWORD") // reuse existing convention
                       ?? Environment.GetEnvironmentVariable("LANTERN_ADMIN_PASSWORD");

        // Clear env vars immediately after reading.
        Environment.SetEnvironmentVariable("LANTERN_ADMIN_USERNAME", null);
        Environment.SetEnvironmentVariable("LANTERN_ADMIN_PASSWORD", null);
        Environment.SetEnvironmentVariable("LANTERN_ADMIN_RESET_PASSWORD", null);

        // Check whether an admin already exists.
        var factory = app.Services.GetRequiredService<IDbContextFactory<LanternDbContext>>();
        await using LanternDbContext db = await factory.CreateDbContextAsync(ct);
        bool hasAdmin = await db.AdministratorAccounts.AnyAsync(ct);

        if (!hasAdmin)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                if (Console.IsInputRedirected)
                {
                    Console.WriteLine("No administrator account exists. Set LANTERN_ADMIN_USERNAME and");
                    Console.WriteLine("LANTERN_ADMIN_PASSWORD environment variables and re-run setup, or");
                    Console.WriteLine("start Lantern with those variables set to provision on first run.");
                }
                else
                {
                    Console.WriteLine();
                    Console.WriteLine("Administrator Setup");
                    Console.WriteLine("-------------------");
                    (username, password) = PromptAdminCredentials();
                }
            }

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                var auth = app.Services.GetRequiredService<AdministratorAuthentication>();
                await auth.ProvisionAsync(username, password, ct);
                Console.WriteLine("Administrator account created.");
            }
        }
        else
        {
            Console.WriteLine("Administrator account already exists — skipped.");
        }
    }

    // ── Status command ────────────────────────────────────────────────────────

    /// <summary>
    /// Reports the health of the Lantern installation.
    /// Returns 0 (healthy), 1 (degraded), or 2 (unavailable).
    /// </summary>
    public static async Task<int> RunStatusAsync(CancellationToken ct = default)
    {
        bool configExists = File.Exists(PlatformDirectories.ProductionConfigFile);
        Console.WriteLine($"Config file   : {(configExists ? "present" : "missing")}  ({PlatformDirectories.ProductionConfigFile})");

        string meiliUrl = "http://127.0.0.1:7700";
        if (configExists)
        {
            try
            {
                await using var fs = File.OpenRead(PlatformDirectories.ProductionConfigFile);
                var doc = await JsonDocument.ParseAsync(fs, cancellationToken: ct);
                if (doc.RootElement.TryGetProperty("Meilisearch", out var meili) &&
                    meili.TryGetProperty("Url", out var urlProp))
                    meiliUrl = urlProp.GetString() ?? meiliUrl;
            }
            catch { }
        }

        bool meiliHealthy = false;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            HttpResponseMessage response = await http.GetAsync($"{meiliUrl}/health", ct);
            meiliHealthy = response.IsSuccessStatusCode;
        }
        catch { }

        Console.WriteLine($"Meilisearch   : {(meiliHealthy ? "healthy" : "unavailable")}  ({meiliUrl}/health)");

        if (!configExists)
        {
            Console.WriteLine("Status: NOT CONFIGURED  (run 'Lantern.Web setup' to initialize)");
            return 2;
        }
        if (!meiliHealthy)
        {
            Console.WriteLine("Status: DEGRADED  (Meilisearch not responding)");
            return 1;
        }
        Console.WriteLine("Status: HEALTHY");
        return 0;
    }

    // ── Admin password reset (interactive) ───────────────────────────────────

    /// <summary>
    /// Interactively prompts for and applies a new administrator password.
    /// </summary>
    public static async Task<int> RunAdminResetPasswordAsync(WebApplication app, CancellationToken ct = default)
    {
        if (Console.IsInputRedirected)
        {
            await Console.Error.WriteLineAsync("Error: 'admin reset-password' requires interactive input.");
            await Console.Error.WriteLineAsync("       For non-interactive use, set LANTERN_ADMIN_RESET_PASSWORD and pass --admin-reset.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("Lantern — Reset Administrator Password");
        Console.WriteLine("--------------------------------------");
        Console.WriteLine();

        string password = PromptPassword("New password (14–128 characters): ");
        string confirm = PromptPassword("Confirm password: ");

        if (password != confirm)
        {
            await Console.Error.WriteLineAsync("Error: passwords do not match.");
            return 1;
        }

        try
        {
            var auth = app.Services.GetRequiredService<AdministratorAuthentication>();
            await auth.ResetPasswordAsync(password, ct);
            Console.WriteLine("Administrator password reset. Existing sessions have been invalidated.");
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            await Console.Error.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }
        finally
        {
            password = string.Empty;
            confirm = string.Empty;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static async Task EnsureDirectoriesAsync(string dataRoot, string secretsDir)
    {
        string[] dirs = OperatingSystem.IsWindows()
            ? [
                dataRoot,
                Path.Combine(dataRoot, "config"),
                Path.Combine(dataRoot, "database"),
                Path.Combine(dataRoot, "meilisearch"),
                Path.Combine(dataRoot, "keys"),
                Path.Combine(dataRoot, "logs"),
                Path.Combine(dataRoot, "backups"),
                secretsDir,
            ]
            : [
                "/etc/lantern",
                secretsDir,
                dataRoot,
                Path.Combine(dataRoot, "database"),
                Path.Combine(dataRoot, "meilisearch"),
                Path.Combine(dataRoot, "keys"),
                Path.Combine(dataRoot, "logs"),
                Path.Combine(dataRoot, "backups"),
            ];

        foreach (string dir in dirs)
        {
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                Console.WriteLine($"  Created : {dir}");
            }
        }

        await Task.CompletedTask;
    }

    private static async Task<string> EnsureMasterKeyAsync(string masterKeyFile, CancellationToken ct)
    {
        if (File.Exists(masterKeyFile))
        {
            Console.WriteLine("  Master key: preserved (already exists)");
            return (await File.ReadAllTextAsync(masterKeyFile, ct)).Trim();
        }

        byte[] bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        string key = Convert.ToBase64String(bytes);
        await File.WriteAllTextAsync(masterKeyFile, key, Encoding.ASCII, ct);
        Console.WriteLine("  Master key: generated");
        return key;
    }

    private static void ApplySecretPermissions(string secretsDir)
    {
        try
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                // Restrict secrets directory to owner only.
                File.SetUnixFileMode(secretsDir,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

                // Restrict any secret files already present.
                foreach (string file in Directory.GetFiles(secretsDir))
                    File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);

                Console.WriteLine("  Secrets directory permissions: 700");
            }
            else if (OperatingSystem.IsWindows())
            {
                ApplyWindowsAcl(secretsDir);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Warning: could not restrict secrets directory permissions: {ex.Message}");
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void ApplyWindowsAcl(string directory)
    {
        // Restrict the secrets directory to the current user only (remove inheritance).
        var di = new System.IO.DirectoryInfo(directory);
        var acl = di.GetAccessControl();
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        string user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
        var rule = new System.Security.AccessControl.FileSystemAccessRule(
            user,
            System.Security.AccessControl.FileSystemRights.FullControl,
            System.Security.AccessControl.InheritanceFlags.ContainerInherit |
            System.Security.AccessControl.InheritanceFlags.ObjectInherit,
            System.Security.AccessControl.PropagationFlags.None,
            System.Security.AccessControl.AccessControlType.Allow);
        acl.SetAccessRule(rule);
        di.SetAccessControl(acl);
        Console.WriteLine("  Secrets directory permissions: restricted to current user.");
    }

    private static async Task EnsureProductionConfigAsync(
        string configFile, string dataRoot, string secretsDir,
        string masterKeyFile, string apiKeyFile, CancellationToken ct)
    {
        if (File.Exists(configFile))
        {
            Console.WriteLine($"  Config: preserved ({configFile})");
            return;
        }

        // Derive install root (parent of app binary directory).
        string appBase = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string installRoot = Path.GetDirectoryName(appBase) ?? appBase;

        string meiliExe = Path.Combine(installRoot,
            OperatingSystem.IsWindows()
                ? Path.Combine("bin", "meilisearch", "meilisearch.exe")
                : Path.Combine("bin", "meilisearch", "meilisearch"));

        string modelPath = Path.Combine(installRoot, "models",
            "granite-embedding-97M-multilingual-r2.gguf");

        var config = new JsonObject
        {
            ["Meilisearch"] = new JsonObject
            {
                ["Url"] = "http://127.0.0.1:7700",
                ["IndexName"] = "lantern-documents",
                ["MaxDocumentsPerBatch"] = 100,
                ["ApiKeyFile"] = apiKeyFile,
            },
            ["MeilisearchProcess"] = new JsonObject
            {
                ["Managed"] = true,
                ["ExecutablePath"] = meiliExe,
                ["DataDirectory"] = Path.Combine(dataRoot, "meilisearch"),
                ["MasterKeyFile"] = masterKeyFile,
                ["BindAddress"] = "127.0.0.1",
                ["Port"] = 7700,
                ["StartupTimeoutSeconds"] = 60,
                ["MaxRestartAttempts"] = 3,
                ["RestartDelaySeconds"] = 5,
            },
            ["Storage"] = new JsonObject
            {
                ["DatabasePath"] = Path.Combine(dataRoot, "database", "lantern.db"),
                ["DataProtectionKeysPath"] = Path.Combine(dataRoot, "keys"),
            },
            ["Embedding"] = new JsonObject
            {
                ["Enabled"] = false,
                ["ModelPath"] = modelPath,
            },
        };

        string? dir = Path.GetDirectoryName(configFile);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await using var stream = File.Create(configFile);
        await JsonSerializer.SerializeAsync(stream, config,
            new JsonSerializerOptions { WriteIndented = true }, ct);

        Console.WriteLine($"  Config written: {configFile}");
    }

    private static (string username, string password) PromptAdminCredentials()
    {
        string username;
        do
        {
            Console.Write("  Username (1–80 characters): ");
            username = Console.ReadLine()?.Trim() ?? "";
        } while (string.IsNullOrEmpty(username) || username.Length > 80);

        string password;
        string confirm;
        do
        {
            password = PromptPassword("  Password (14–128 characters): ");
            confirm = PromptPassword("  Confirm password: ");
            if (password != confirm)
                Console.Error.WriteLine("  Passwords do not match — try again.");
            else if (password.Length < 14 || password.Length > 128)
            {
                Console.Error.WriteLine("  Password must be 14–128 characters — try again.");
                password = "";
            }
        } while (password != confirm || password.Length < 14);

        return (username, password);
    }

    private static string PromptPassword(string prompt)
    {
        Console.Write(prompt);
        var sb = new StringBuilder();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); break; }
            if (key.Key == ConsoleKey.Backspace && sb.Length > 0)
            {
                sb.Remove(sb.Length - 1, 1);
                Console.Write("\b \b");
            }
            else if (key.Key != ConsoleKey.Backspace)
            {
                sb.Append(key.KeyChar);
                Console.Write('*');
            }
        }
        return sb.ToString();
    }

    private static string? GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }
}
