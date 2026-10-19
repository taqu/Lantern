# Administration (Phase 3)

Lantern now provides `/admin`, `/admin/crawlers`, `/admin/jobs`, `/admin/index`,
`/admin/settings` and `/admin/system`. Public `/search` access is unchanged. The search
page remains Interactive Server; administration and account pages are static,
server-rendered Blazor components using standard HTTP forms. Every privileged request
therefore passes through cookie validation and authorization, with no long-lived admin
circuit retaining stale authorization after logout.

## First administrator and HTTPS

Use the existing HTTPS development profile:

```powershell
dotnet run --project src/Lantern.Web --launch-profile https
```

Visit `https://localhost:7108/account/login`. Trust the .NET development certificate
if your local development setup requires it. Administration uses secure-only cookies
even in Development; HTTP is not a supported admin login transport.

Before the first start, provision the account using server-side process environment
variables `LANTERN_ADMIN_USERNAME` and `LANTERN_ADMIN_PASSWORD`. The password must have
14–128 characters and must not be a publicly shared value. For a local PowerShell
session, enter it without putting the password in shell history:

```powershell
$env:LANTERN_ADMIN_USERNAME = Read-Host 'Initial administrator username'
$initialPassword = Read-Host 'Initial administrator password' -AsSecureString
$env:LANTERN_ADMIN_PASSWORD = [System.Net.NetworkCredential]::new('', $initialPassword).Password
try {
    dotnet run --project src/Lantern.Web --launch-profile https
} finally {
    Remove-Item Env:LANTERN_ADMIN_PASSWORD -ErrorAction SilentlyContinue
    Remove-Item Env:LANTERN_ADMIN_USERNAME -ErrorAction SilentlyContinue
    $initialPassword.Dispose()
}
```

Remove the bootstrap variables from the hosting environment after the first successful
initialization. An existing account is never replaced by bootstrap environment changes.
Without both variables, a fresh installation starts with no usable administrator account;
there is no public setup or registration endpoint. Supplying incomplete or invalid initial
credentials fails startup with an actionable error. No administrator password is shipped
in configuration or source control. Tests provision only their isolated temporary database.

The account table stores an ASP.NET Core `PasswordHasher` result, never a plaintext
password. It is separate from editable application settings. Provisioning, login and
session validation keep this entity entirely server-side. SQLite is not encrypted by
Lantern; restrict access to its directory and backups to the application identity and
authorized operators. Process environment variables must be managed as server secrets.

Login failures use a generic message. Five failed credentials attempts lock the single
account for 15 minutes, persisted in SQLite. Login requests are also limited to 10 per
minute per connecting IP, with no queue. This can temporarily lock out the legitimate
administrator; there is no account recovery or password-change UI in Phase 3. Configure
the first account deliberately and retain credentials in an approved password manager.

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

## Verification and references

```powershell
dotnet build Lantern.slnx --artifacts-path artifacts/phase3
dotnet test Lantern.slnx --artifacts-path artifacts/phase3 --no-build
```

SQLite and HTTP tests use isolated temporary directories. HTTP integration tests run the
real application middleware with HTTPS test clients, actual cookies, real SQLite,
antiforgery tokens and an intentionally unavailable Meilisearch endpoint. They cover
authorization, settings, logout/replay, account/key persistence after host restart,
rate limits and offline administration. Existing real Meilisearch tests also exercise
the new health service and verify actual document counts when `MEILI_MASTER_KEY` is set.

The implementation follows Microsoft's guidance for [Blazor authentication and
authorization](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/?view=aspnetcore-10.0),
[antiforgery protection](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery),
and [applying EF migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying).
