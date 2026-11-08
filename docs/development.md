# Lantern — Developer Guide

This guide covers everything needed to build, debug, and run Lantern from source on Windows.

---

## Prerequisites

| Requirement | Version | Notes |
|---|---|---|
| .NET SDK | 10.0 | `dotnet --version` must start with `10.` |
| Windows | 10 or 11 | x64 required for LlamaPure native libraries |
| Visual Studio | 2022 17.12+ | For F5 debugging; Community edition supported |
| VS Code | Latest | Alternative IDE; install C# Dev Kit extension |
| Meilisearch | v1.x | Downloaded separately — see below |

---

## Quick Start

```powershell
# 1. Clone
git clone <repo-url>
cd Lantern

# 2. Download Meilisearch (once)
.\scripts\download-meilisearch.ps1

# 3. One-time development setup
.\scripts\setup-dev.ps1

# 4a. Visual Studio: open Lantern.slnx, select "Lantern Development" profile, press F5
# 4b. CLI: .\scripts\start-dev.ps1
```

---

## 1. Repository Checkout

Clone the repository. No special flags required.

```powershell
git clone <repo-url> Lantern
cd Lantern
```

---

## 2. .NET SDK Setup

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) for Windows x64.

Verify:

```powershell
dotnet --version   # must be 10.x.x
```

---

## 3. Meilisearch

Meilisearch is not bundled in the repository. Download the Windows x64 binary once:

```powershell
.\scripts\download-meilisearch.ps1
```

This places `meilisearch.exe` in `tools\meilisearch\`. The binary and its data files are gitignored.

The application starts and manages Meilisearch automatically in Development mode. A dedicated development data directory (`App_Data\meilisearch\`) is used, completely separate from any production installation.

---

## 4. Granite GGUF Model (Optional)

Hybrid search requires the Granite embedding model:

```
resources\granite-embedding-97M-multilingual-r2.gguf
```

If this file is absent, the application starts normally with keyword-only search. The model is not downloaded automatically.

To enable hybrid search, place the model file in `resources\` and set `Embedding:Enabled = true` in `appsettings.Development.Local.json` (created by setup-dev.ps1).

---

## 5. Development Setup (One Time)

Run the setup script once after cloning:

```powershell
.\scripts\setup-dev.ps1
```

This script:

1. Validates the .NET 10 SDK.
2. Checks for `meilisearch.exe`.
3. Creates `src\Lantern.Web\App_Data\` with subdirectories.
4. Generates a Meilisearch master key stored at `App_Data\secrets\meilisearch-master-key.txt`.
5. Writes `src\Lantern.Web\appsettings.Development.Local.json` with absolute paths to the Meilisearch binary, data directory, and key file. This file is gitignored.
6. Stores development administrator credentials in [User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets).
7. Trusts the ASP.NET Core HTTPS development certificate.

The script is safe to run multiple times. Existing secrets and configuration are preserved.

**Save the administrator password** printed during first run. If you need to change it later, use the `-ResetAdmin` flag — do not delete the database:

```powershell
.\scripts\setup-dev.ps1 -ResetAdmin
```

---

## 6. Development Administrator Provisioning

On first application start, the initial administrator account is created from credentials stored in User Secrets (`DevAdmin:Username` / `DevAdmin:Password`), set by `setup-dev.ps1`.

The account is created **only once** — on the first startup after `setup-dev.ps1` has stored credentials and no account exists in the database. Subsequent startups skip provisioning and use the existing account. This means the credentials printed by `setup-dev.ps1` on its first run are the ones that apply; re-running the script does not change the database password.

If you need to change the password, use the credential reset command:

```powershell
.\scripts\setup-dev.ps1 -ResetAdmin
```

This generates a new password, updates the database using the application's own hashing, rotates the session stamp (invalidating existing sessions), and stores the new password in User Secrets.

Environment variables `LANTERN_ADMIN_USERNAME` and `LANTERN_ADMIN_PASSWORD` take precedence over User Secrets if both are set.

---

## 7. Visual Studio Debugging (F5)

1. Open `Lantern.slnx` in Visual Studio 2022.
2. Set `Lantern.Web` as the startup project (right-click → Set as Startup Project).
3. Select the **Lantern Development** launch profile from the run dropdown.
4. Press **F5**.

The application starts at `https://localhost:7108`. Meilisearch starts automatically as a managed process. SQLite migrations run automatically.

**Note:** Visual Studio F5 debugging requires that `.\scripts\setup-dev.ps1` has been run first so that `appsettings.Development.Local.json` exists with the correct paths.

---

## 8. VS Code Debugging

1. Install the **C# Dev Kit** extension (ms-dotnettools.csdevkit).
2. Open the repository root folder in VS Code.
3. Run `.\scripts\setup-dev.ps1` from the integrated terminal if not done already.
4. Press **F5** and select **Lantern Development**.

The `.vscode/launch.json` configuration uses `cwd: src/Lantern.Web` and sets `ASPNETCORE_ENVIRONMENT=Development`.

---

## 9. CLI Development Startup

```powershell
.\scripts\start-dev.ps1
```

This runs `dotnet run --project src\Lantern.Web --launch-profile "Lantern Development"` with `ASPNETCORE_ENVIRONMENT=Development`.

---

## 10. Development Data Isolation

All development data lives under `src\Lantern.Web\App_Data\`:

```
App_Data\
    database\       SQLite database (lantern.db)
    keys\           ASP.NET Core data protection keys
    meilisearch\    Meilisearch data directory
    secrets\        Meilisearch master key file
    logs\           Application logs (if configured)
```

This directory is gitignored. It is completely separate from any production installation at `C:\ProgramData\Lantern\`.

To specify a different data root, set `Storage:DatabasePath` and `Storage:DataProtectionKeysPath` in `appsettings.Development.Local.json`.

---

## 11. Development Ports

| Service | Development | Production default |
|---|---|---|
| Lantern Web (HTTPS) | 7108 | configured by admin |
| Lantern Web (HTTP) | 5271 | — |
| Meilisearch | 7702 | 7700 |
| Embedding REST | 7703 | 7701 |

Development and production ports differ to avoid conflicts if both run on the same machine.

---

## 12. SQLite Database and Migrations

The development database is created automatically on first launch:

```
App_Data\database\lantern.db
```

EF Core migrations run automatically at application startup. No manual migration steps are required for normal development.

To apply or create migrations manually:

```powershell
# Apply migrations to the development database
$env:LANTERN_MIGRATION_DATABASE = "src\Lantern.Web\App_Data\database\lantern.db"
dotnet ef database update --project src\Lantern.Infrastructure --startup-project src\Lantern.Web

# Create a new migration
dotnet ef migrations add MyMigrationName --project src\Lantern.Infrastructure --startup-project src\Lantern.Web
```

To reset the development database, delete `App_Data\database\lantern.db`. It will be recreated on next startup. This also removes the administrator account; re-run `setup-dev.ps1` to restore it.

---

## 13. Meilisearch Process Management

In Development mode, the application manages Meilisearch automatically:

- **Startup**: `MeilisearchProcessManager` launches `meilisearch.exe` with `--db-path App_Data\meilisearch --http-addr 127.0.0.1:7702`. It polls `http://127.0.0.1:7702/health` until ready (up to 60 seconds). `FailOnStartupError` is `false` in development, so search is unavailable rather than blocking app startup if Meilisearch is missing.
- **Shutdown**: The process is closed gracefully when the application stops; killed if it doesn't exit within 5 seconds.
- **Monitoring**: The process is monitored every 10 seconds and restarted up to 3 times if it crashes.

To start Meilisearch independently (bypasses application management):

```powershell
$env:MEILI_MASTER_KEY = Get-Content src\Lantern.Web\App_Data\secrets\meilisearch-master-key.txt
.\scripts\start-meilisearch.ps1 -Port 7702 -DataDir src\Lantern.Web\App_Data\meilisearch
```

---

## 14. Embedding Server Troubleshooting

The embedding server is disabled by default in Development (`Embedding:Enabled = false`).

To enable it, add to `appsettings.Development.Local.json`:

```json
{
  "Embedding": {
    "Enabled": true,
    "ModelPath": "resources/granite-embedding-97M-multilingual-r2.gguf"
  },
  "HybridSearch": {
    "Enabled": true
  }
}
```

**Native library loading:** `LlamaPure` loads native llama.cpp DLLs bundled with its NuGet package. These are automatically extracted to the build output directory by the NuGet restore process. No manual DLL installation is required.

**Common embedding errors:**

| Error | Cause | Fix |
|---|---|---|
| `Model file not found` | GGUF model not at configured path | Place model file in `resources\` |
| `DllNotFoundException` | Native DLLs not in build output | Run `dotnet build` to trigger NuGet restore |
| `Embedding service not ready` | Model initialization in progress | Wait 10–30 seconds after startup |

---

## 15. Running Tests

```powershell
# All tests
dotnet test

# Unit tests only
dotnet test tests\Lantern.Core.Tests

# Integration tests (requires Meilisearch)
dotnet test tests\Lantern.IntegrationTests
```

---

## 16. Resetting the Development Administrator Password

To change the administrator password without touching any other data:

```powershell
.\scripts\setup-dev.ps1 -ResetAdmin
```

This:
1. Verifies the development database exists.
2. Generates a new password using the application's own password hasher.
3. Updates the administrator record in the database.
4. Rotates the session stamp, invalidating any active browser sessions.
5. Stores the new password in User Secrets.
6. Displays the new password once.

**Resetting all development data**

Delete the database file only if you want to reset all application data (crawlers, search data, job history, settings, and the administrator account):

```powershell
Remove-Item src\Lantern.Web\App_Data\database\lantern.db -Force
```

The database and administrator account are recreated on next application start using the credentials currently stored in User Secrets. Meilisearch indexes are **not** cleared; delete `App_Data\meilisearch\` separately to clear them.

If the User Secrets no longer match what you want, regenerate them before restarting:

```powershell
dotnet user-secrets remove "DevAdmin:Username" --project src\Lantern.Web
dotnet user-secrets remove "DevAdmin:Password" --project src\Lantern.Web
.\scripts\setup-dev.ps1
```

---

## 17. Debugging Background Services

Lantern runs several hosted services:

- `MeilisearchProcessManager` — Meilisearch lifecycle
- `InferenceWorker` — LlamaPure inference loop (when embedding enabled)
- `EmbeddingServer` — Loopback HTTP embedding endpoint (when enabled)
- `FileCrawlerService` — File system crawling
- Job dispatch and scheduling services

All services use structured logging with named categories (e.g., `Lantern.Web.MeilisearchProcessManager`). Set log level to `Debug` in `appsettings.Development.json` for verbose output.

**Debugger pauses:** No background service health checks impose deadlines that would misinterpret a paused debugger as a failure. Meilisearch startup has a 60-second timeout, which is not affected by debugger pauses after the app has started.

---

## 18. Common Errors

| Error | Likely cause | Fix |
|---|---|---|
| `Could not initialize Lantern administration` | App_Data directory permissions or missing migrations | Check `App_Data\database\` exists and is writable |
| `Meilisearch executable not found` | Setup not run or Meilisearch not downloaded | Run `download-meilisearch.ps1` then `setup-dev.ps1` |
| `appsettings.Development.Local.json not found warning` (startup) | Setup not run | Run `.\scripts\setup-dev.ps1` |
| Login page accepts credentials but doesn't authenticate | Cookie requires HTTPS | Use `https://localhost:7108` not `http://localhost:5271` |
| `No valid administrator account` | No account created yet | Verify setup-dev.ps1 ran; check `DevAdmin:*` User Secrets |
| Login fails after re-running setup-dev.ps1 | setup-dev.ps1 preserved old credentials correctly; previous password still applies | Use `.\scripts\setup-dev.ps1 -ResetAdmin` to generate a new password |
| Native DllNotFoundException | Build not restored | Run `dotnet build src\Lantern.Web` |

---

## 19. Differences Between Development and Production

| Aspect | Development | Production |
|---|---|---|
| Data directory | `src\Lantern.Web\App_Data\` | `C:\ProgramData\Lantern\` |
| Database | `App_Data\database\lantern.db` | `ProgramData\Lantern\database\lantern.db` |
| Meilisearch port | 7702 | 7700 |
| Meilisearch index | `lantern-dev-documents` | `lantern-documents` |
| Meilisearch management | Managed (auto-start) | Managed (via service) |
| Admin provisioning | User Secrets (`DevAdmin:*`) | Env vars / `Initialize-Lantern.ps1` |
| Configuration | `appsettings.Development.json` + User Secrets + `appsettings.Development.Local.json` | `C:\ProgramData\Lantern\config\appsettings.json` |
| Developer exception page | Enabled | Disabled |
| HTTPS redirect | Active | Active |
| Windows Service | Not registered | Registered as "Lantern" |

---

## 20. Hot Reload

ASP.NET Core Blazor Server hot reload is available when using `dotnet watch`:

```powershell
dotnet watch --project src\Lantern.Web
```

Hot reload support varies by component type. Razor component changes generally apply without restart. Changes to `Program.cs` or service registrations require a manual restart.

Hot reload is not verified in Visual Studio and VS Code F5 sessions; use `dotnet watch` explicitly for hot reload.
