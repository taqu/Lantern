# Phase 3 implementation report

## A. Implementation summary

Implemented SQLite persistence with EF Core, a tracked initial migration and context
factory, persisted application display name, minimal file/web crawler definitions,
administrator provisioning, login/logout, protected administration pages and actions,
Meilisearch health and document counts, and system information. Public search and the
Phase 2 indexing API remain intact. No crawling, extraction, scheduling or jobs were added.

Administration routes provide Overview, Crawlers, Jobs, Search Index, Settings and
System Status. Meilisearch outages produce safe status messages while database-backed
pages and login remain available. The editable display name affects navigation on the
next request; deployment-owned connection options are explicitly read-only.

## B. Files changed

Created:

- `src/Lantern.Core/Administration/ApplicationSetting.cs`
- `src/Lantern.Core/Administration/CrawlerDefinition.cs`
- `src/Lantern.Core/Administration/SearchEngineStatus.cs`
- `src/Lantern.Infrastructure/Persistence/AdministratorAccount.cs`
- `src/Lantern.Infrastructure/Persistence/ApplicationSettingsService.cs`
- `src/Lantern.Infrastructure/Persistence/LanternDbContext.cs`
- `src/Lantern.Infrastructure/Persistence/LanternDbContextFactory.cs`
- `src/Lantern.Infrastructure/Persistence/Migrations/20261019123757_InitialAdministration.cs`
- `src/Lantern.Infrastructure/Persistence/Migrations/20261019123757_InitialAdministration.Designer.cs`
- `src/Lantern.Infrastructure/Persistence/Migrations/LanternDbContextModelSnapshot.cs`
- `src/Lantern.Infrastructure/Search/MeilisearchHealthService.cs`
- `src/Lantern.Web/Administration/AdministrationSetup.cs`
- `src/Lantern.Web/Administration/AdministrationService.cs`
- `src/Lantern.Web/Administration/AdministrationEndpoints.cs`
- `src/Lantern.Web/Administration/AdministratorAuthentication.cs`
- `src/Lantern.Web/Components/Layout/AdminLayout.razor`
- `src/Lantern.Web/Components/Pages/Admin/_Imports.razor`
- `src/Lantern.Web/Components/Pages/Admin/Status.razor`
- `src/Lantern.Web/Components/Pages/Admin/Settings.razor`
- `src/Lantern.Web/Components/Pages/Admin/Crawlers.razor`
- `src/Lantern.Web/Components/Pages/Admin/Jobs.razor`
- `src/Lantern.Web/Components/Pages/Account/Login.razor`
- `src/Lantern.Web/Components/Pages/Account/Denied.razor`
- `tests/Lantern.Core.Tests/AdministrationModelTests.cs`
- `tests/Lantern.IntegrationTests/AdministrationHttpTests.cs`
- `tests/Lantern.IntegrationTests/SearchEngineHealthTests.cs`
- `tests/Lantern.IntegrationTests/SqlitePersistenceTests.cs`
- `docs/administration.md`
- `docs/work/phase3_recap.md`

Modified during this phase:

- `.gitignore` — exclude local application database/key storage.
- `README.md`, `docs/architecture.md`, `docs/dependencies.md` — current capabilities and setup.
- `src/Lantern.Infrastructure/Lantern.Infrastructure.csproj` — EF SQLite/design dependencies,
  aligned Microsoft.Extensions versions, updated SQLite native bundle.
- `src/Lantern.Infrastructure/ServiceCollectionExtensions.cs` — shared-client health registration.
- `src/Lantern.Web/Program.cs` — persistence initialization, authentication and endpoints.
- `src/Lantern.Web/Components/Routes.razor` — authorized routing.
- `src/Lantern.Web/Components/Layout/NavMenu.razor` — persisted name and administration link.
- `tests/Lantern.IntegrationTests/Lantern.IntegrationTests.csproj` — real application HTTP test host.
- `tests/Lantern.IntegrationTests/IndexingIntegrationTests.cs` — real health/count assertions.

Earlier user and Phase 2 changes were preserved, including the work instructions,
download script, feasibility project changes and indexing implementation. They were
already present in the uncommitted worktree at the beginning of Phase 3.

## C. Architectural decisions

- Paths: development uses content-root `App_Data`; Windows production uses
  `%ProgramData%/Lantern`. Database/key paths are separately configurable, outside
  `wwwroot`, and documented with directory permission requirements.
- Schema: singleton settings and administrator rows, GUID crawler definitions, explicit
  SQLite check constraints and indexes. The generated migration and snapshot are tracked.
  Startup migrates without recreating data; failures stop startup with diagnostics.
- Lifetimes: `IDbContextFactory` supplies one context per operation. There is no generic
  repository or shared component context.
- Settings: SQLite owns the display name only. Existing server configuration owns the
  endpoint, index and ingestion options. No inert or conflicting editable settings exist.
- Provisioning: server process environment supplies the initial username and password
  only when no account exists. ASP.NET Core PasswordHasher uses 210,000 iterations.
  Existing accounts cannot be overwritten by changed bootstrap variables.
- Authentication: secure-only, HttpOnly, SameSite Strict cookies, 30-minute expiry,
  persisted Data Protection keys (DPAPI-protected on Windows), account lockout and
  per-IP login request limits. Rotating a persisted session stamp revokes old cookies;
  only one active administrator session is supported.
- Authorization: static SSR admin pages enforce the administrator policy through the
  HTTP middleware. Every privileged action endpoint also requires that policy and
  antiforgery validation, and the admin service independently demands authorization.
  Public search remains Interactive Server and anonymous.
- Secrets: bootstrap credentials are server-only; SQLite holds a password hash in a
  separate account entity. No plaintext password or Meilisearch key is stored in settings,
  returned to components, logged, or populated into forms.
- Monitoring: the official SDK and existing DI client supply health and index statistics.
  Requests have a five-second deadline and ten-second cache. Monitoring does not create
  or alter indexes, and distinguishes zero documents from unknown count.

Operational instructions are in [administration.md](../administration.md).

## D. Commands executed

Baseline:

```powershell
dotnet build Lantern.slnx --artifacts-path artifacts\phase3 --verbosity quiet
dotnet test Lantern.slnx --artifacts-path artifacts\phase3 --no-build
```

Dependency, migration and targeted checks:

```powershell
dotnet restore Lantern.slnx --artifacts-path artifacts\phase3
dotnet tool install dotnet-ef --tool-path artifacts\tools --version 10.0.13
dotnet build src\Lantern.Web --artifacts-path artifacts\phase3 --verbosity quiet
artifacts\tools\dotnet-ef migrations add InitialAdministration --project src\Lantern.Infrastructure --output-dir Persistence\Migrations
dotnet test tests\Lantern.IntegrationTests --artifacts-path artifacts\phase3 --filter "FullyQualifiedName~SqlitePersistenceTests|FullyQualifiedName~AdministrationHttpTests" --verbosity quiet
dotnet test tests\Lantern.IntegrationTests --artifacts-path artifacts\phase3 --filter "FullyQualifiedName~AdministrationHttpTests|FullyQualifiedName~SearchEngineHealthTests" --logger "console;verbosity=minimal"
dotnet test tests\Lantern.IntegrationTests --artifacts-path artifacts\phase3 --filter "FullyQualifiedName~AdministrationHttpTests|FullyQualifiedName~SearchEngineHealthTests|FullyQualifiedName~SqlitePersistenceTests" --logger "console;verbosity=minimal"
dotnet test tests\Lantern.IntegrationTests --artifacts-path artifacts\phase3 --filter FullyQualifiedName~AdministrationHttpTests --logger "console;verbosity=minimal"
dotnet test tests\Lantern.IntegrationTests --artifacts-path artifacts\phase3 --no-build --filter FullyQualifiedName~AdministrationHttpTests --logger "console;verbosity=minimal"
dotnet test tests\Lantern.IntegrationTests --artifacts-path artifacts\phase3 --filter FullyQualifiedName~SearchEngineHealthTests --logger "console;verbosity=minimal"
dotnet test tests\Lantern.IntegrationTests --artifacts-path artifacts\phase3 --filter FullyQualifiedName~MissingIndexDoesNotCreateOne --logger "console;verbosity=minimal"
dotnet test tests\Lantern.IntegrationTests --artifacts-path artifacts\phase3 --no-build --filter FullyQualifiedName~LoginSettingsAntiforgeryLogoutAndCookieReplay --logger "console;verbosity=normal"
dotnet test Lantern.slnx --artifacts-path artifacts\phase3 --logger "console;verbosity=minimal"
artifacts\tools\dotnet-ef migrations has-pending-model-changes --project src\Lantern.Infrastructure --no-build
artifacts\tools\dotnet-ef migrations has-pending-model-changes --project src\Lantern.Infrastructure
```

Final verification:

```powershell
dotnet build Lantern.slnx --artifacts-path artifacts\phase3 --no-restore --verbosity quiet
dotnet test Lantern.slnx --artifacts-path artifacts\phase3 --no-build --logger "console;verbosity=minimal"
git diff --check
```

EF tool download and a targeted HTTP test run were rerun with approved access outside
the sandbox after network/Windows facility restrictions. No production database was
initialized, no real administrator was provisioned, and the existing running application
was left untouched. Test databases and keys used unique temporary directories.

## E. Test results

- Baseline: build succeeded; 47 Core tests and 17 integration-project tests passed.
- Final solution build: passed, zero errors. Five pre-existing ImageSharp dependency
  warnings remain in the feasibility tool. No new dependency vulnerability warnings remain.
- Final Core suite: **52 passed, 0 failed, 0 skipped**.
- Final integration project: **29 passed, 0 failed, 0 skipped**.
- Real Meilisearch operations ran: existing indexing/search coverage plus health reporting
  and the actual six-document count on an isolated test index.
- Real SQLite tests verified migration creation/application, constraints, crawler persistence,
  setting insertion/update, defaults, validation and repeated initialization without data loss.
- HTTP tests verified all admin routes, direct privileged endpoints, anonymous/ordinary-user
  denial, valid/invalid login, antiforgery, rate limits, server validation, offline connection
  testing, public search, logout, rejected old-cookie replay, and persisted settings/account/
  Data Protection keys after a complete test-host restart.
- EF's final check reports **no model changes since the last migration**.
- `git diff --check` passed.

Intermediate failures were corrected: an ambiguous login POST route was split from the
Blazor GET page; health handling now supports both ordinary HTTP and SDK-wrapped errors;
test logging avoids Windows Event Log dependence. The initial package version attempt
was updated/aligned to 10.0.13, with SQLitePCLRaw 3.0.5 removing the older native SQLite
warning. A stale default build initially reported pending model changes with `--no-build`;
the rebuilt tooling check and SQLite model snapshot assertion both confirm no drift.

Not executed: manual browser visual testing, deployment under a Windows service identity,
production migration, production administrator provisioning, and proxy/TLS deployment.
The UI and session flows were exercised through the real application HTTP middleware.

## F. Known limitations

- One administrator and one active session; no user management, password-change/recovery
  UI, or multi-process coordination. Lockout can temporarily deny the legitimate user.
- Administration requires HTTPS. Reverse-proxy forwarding and production directory ACLs
  are deployment responsibilities; Windows DPAPI key access is tied to the service account.
- SQLite and backups need filesystem protection; the database itself is not encrypted.
- Startup applies migrations. Back up data and coordinate future upgrades; never delete a
  database or migration lock blindly to work around a failed upgrade.
- Connection/index options remain deployment-owned and require restart. Only the display
  name is editable; no unsupported settings are presented as live-editable.
- Crawler and job management/execution remain deliberately unavailable.
- Existing Phase 0 extraction dependency warnings were not changed.

## G. Remaining work

No outstanding Phase 3 implementation or automated acceptance checks. Manual visual and
production-environment smoke checks remain deployment follow-up work. No later phase
was started, and no production environment was changed.
