# Lantern Administration Reference

Lantern provides `/admin`, `/admin/crawlers`, `/admin/jobs`, `/admin/index`,
`/admin/settings`, and `/admin/system`. Public `/search` access is unchanged. The search
page uses Interactive Server rendering; administration and account pages are static,
server-rendered Blazor components using standard HTTP forms. Every privileged request
therefore passes through cookie validation and authorization, with no long-lived admin
circuit retaining stale authorization after logout.

## Initial Administrator Setup

### Release installation

The initial administrator account is created interactively during the first run of
`start-lantern.ps1`. The script prompts for a username (1–80 characters) and a password
(14–128 characters with confirmation). No account is created automatically on subsequent
launches.

```powershell
.\start-lantern.ps1
```

See [`docs/INSTALL.md`](INSTALL.md) for the complete first-run workflow.

### Development environment

For development, `scripts/setup-dev.ps1` generates a random password and stores
credentials in User Secrets (`DevAdmin:Username` / `DevAdmin:Password`). These are read
at startup via `IConfiguration` and used to provision the administrator on first launch.

```powershell
.\scripts\setup-dev.ps1
```

See [`docs/development.md`](development.md) for the complete development workflow.

### Provisioning rules

- An administrator account is created **only once** — when no account exists in the
  database and credentials are provided. Subsequent launches skip provisioning entirely.
- The account is stored in SQLite. The password is hashed with PBKDF2 (HMAC-SHA512,
  210,000 iterations) via ASP.NET Core `PasswordHasher<T>`. Plaintext passwords are
  never persisted.
- There is no public registration or setup endpoint. Credentials must be supplied through
  the local setup mechanism.

### Credential recovery

Use `reset-admin.ps1` (included in the release package) to reset the administrator
password without deleting application data:

```powershell
.\reset-admin.ps1
```

See the [Password Recovery](#password-recovery) section below.

## Login and Sessions

Administration uses secure-only cookies.
HTTP is not a supported login transport unless the connection is to loopback
(`127.0.0.1` / `[::1]`).

Login failures use a generic message to prevent account enumeration. Five consecutive
failed attempts lock the account for 15 minutes, persisted in SQLite. Login requests
are rate-limited to 10 per minute per connecting IP.

## Sessions and authorization

Authentication uses ASP.NET Core cookie authentication and Data Protection. The cookie
is HttpOnly, Secure, SameSite Strict, scoped to the host, nonpersistent, and expires
after 30 minutes without sliding renewal. A server-side session stamp is checked on
every authenticated request. Signing in rotates the stamp; only one active administrator
session is supported. Signing out rotates it again, so replaying the old cookie is
rejected even before its expiry.

All admin pages and action endpoints require the `Administrator` policy (authenticated
identity plus administrator role). `AdministrationService` also checks this policy for
each read/write operation. Public settings reads return only the nonsecret display name.
Privileged HTTP forms manually validate ASP.NET Core antiforgery tokens before acting;
login is protected as well. Login redirects only to the fixed local `/admin` destination.
No supplied return URL is followed. GET requests do not change settings or log out.

Deployment requires HTTPS at Lantern. TLS termination/forwarding must be configured
explicitly for a trusted proxy before deploying behind one; forwarded headers are not
blindly trusted. The IP limiter sees the connecting proxy if forwarding is not configured.
Do not use the Development environment on a deployed server.

## Data paths and migrations

Development defaults, relative to `src/Lantern.Web`:

```text
App_Data/database/lantern.db
App_Data/keys/
```

Production defaults on Windows:

```text
%ProgramData%/Lantern/database/lantern.db
%ProgramData%/Lantern/keys/
```

`App_Data` is ignored by Git. Override paths with `Storage:DatabasePath` and
`Storage:DataProtectionKeysPath` (or `Storage__DatabasePath` and
`Storage__DataProtectionKeysPath` environment variables). Relative paths resolve against
the application content root. Necessary directories are created; locations beneath
`wwwroot` are rejected. Provision directory ACLs so the service identity can read/write
them and other untrusted users cannot. The application does not grant global access.

Authentication keys persist across restarts. Windows keys are encrypted with DPAPI for
the current Windows account; changing the service identity requires planning for key
access or accepting session invalidation. On non-Windows systems, restrict key-directory
permissions or configure an appropriate Data Protection encryption provider before
deployment. Do not put the key directory in web-accessible storage.

Startup calls `MigrateAsync` before accepting traffic and fails on errors, preserving
the existing database. It never uses `EnsureCreated`, deletes the database, or recreates
tables to resolve errors. Back up the database before future upgrades, using a SQLite-safe
backup or a stopped application (account for WAL files). Run one Lantern application
instance per database; there is no multi-process authentication/write coordination.

The tracked `InitialAdministration` migration and model snapshot create:

- A singleton application settings row with a constrained display name.
- Crawler definitions with GUID identity, file/web type, enabled flag and UTC timestamps.
- A singleton administrator account with a password hash, session stamp and lockout state.

The context factory creates a fresh context for every service operation, avoiding shared
contexts in Blazor. EF operations are parameterized; validation and database check
constraints reject invalid persisted values. No extracted text or indexed documents are
stored in SQLite.

For explicit migration tooling (not required for normal startup):

```powershell
dotnet tool install dotnet-ef --tool-path artifacts/tools --version 10.0.13
$env:LANTERN_MIGRATION_DATABASE = 'D:\LanternData\database\lantern.db'
artifacts/tools/dotnet-ef database update --project src/Lantern.Infrastructure
artifacts/tools/dotnet-ef migrations has-pending-model-changes --project src/Lantern.Infrastructure
```

Create the target parent directory first. The design-time factory uses only
`LANTERN_MIGRATION_DATABASE`; set it to the exact deployed database path when applying
migrations manually. If omitted, tooling targets a temporary `lantern-design.sqlite`,
not the deployed application database. Future model changes require a tracked migration:

```powershell
artifacts/tools/dotnet-ef migrations add MeaningfulChange --project src/Lantern.Infrastructure --output-dir Persistence/Migrations
```

## Settings ownership and monitoring

| Value | Owner | Applying changes |
|---|---|---|
| Application display name | SQLite via `/admin/settings` | Immediately on the next navigation request |
| Meilisearch endpoint, index, batch and task limits | Existing `Meilisearch` deployment section | Restart |
| Meilisearch credential | Server environment `MEILI_API_KEY` | Restart |
| Database/key directories | `Storage` deployment configuration | Restart; move data deliberately |
| Initial account | Server bootstrap variables, only while account is absent | First initialization only |

No secrets can be edited or read through the settings form. The form displays whether
the search credential is configured; endpoint display strips user information, query
and fragments. Configuration follows ASP.NET Core's existing deployment/environment
precedence. SQLite owns only the display name and does not override Meilisearch options.
Search pagination retains its existing default; no unused editable preferences were added.

Overview, Search Index and System Status display real health, index name, document count,
database connectivity, runtime, environment, version and process uptime. They reuse one
existing Meilisearch client and the configured index. Health reads are cached for 10
seconds, time out after five seconds, and distinguish an empty index (zero) from an
unavailable or missing index (unknown). The Test Connection action forces a server-side
refresh. Monitoring never initializes, clears or recreates an index. A Meilisearch outage
does not prevent access to settings, crawler definitions or account login.

Crawlers is a read-only list of persisted definitions with a clear scope notice. Jobs
is an informational page. Creation/editing, execution, scheduling, history and extraction
remain later-phase features. No destructive index management controls were added.

## Password Recovery

Use `reset-admin.ps1` (at the root of the release package) to reset the administrator
password without affecting any other application data:

```powershell
.\reset-admin.ps1
```

The script:
1. Stops the Windows Service if running.
2. Prompts for a new password twice (input is hidden).
3. Passes the password via a process environment variable to `Lantern.Web.exe --admin-reset`.
4. Lantern hashes the new password with its own `PasswordHasher` implementation and updates SQLite.
5. Rotates the session stamp, invalidating all active browser sessions.
6. Offers to restart the Windows Service.

**Data preserved during reset:** crawler configurations, search categories, job history,
schedules, Meilisearch indexes, and all other SQLite records are unaffected.

If the data directory is not `C:\ProgramData\Lantern\`, specify it explicitly:

```powershell
.\reset-admin.ps1 -DataRoot D:\YourDataPath
```

The Windows Service must be stopped before running the reset to prevent concurrent
database writes. The script handles this automatically; run it from an elevated
PowerShell prompt if the service requires elevation to stop.

For development password reset, see `docs/development.md` section 16.

## Running Tests

```powershell
dotnet build Lantern.slnx
dotnet test Lantern.slnx --no-build
```

Integration tests use isolated temporary directories with real SQLite and no Meilisearch
dependency. They cover authorization, settings, logout/session replay, account persistence
after host restart, rate limits, and antiforgery protection.
