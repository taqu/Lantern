# Lantern — Phase 3 Implementation Instructions
## Administration Foundation

You are implementing **Phase 3: Administration Foundation** of the Lantern project.

Lantern is a lightweight, self-hosted full-text search platform built with .NET 10, ASP.NET Core, Blazor Web App (Interactive Server), Meilisearch Community Edition, and SQLite.

Its primary target is Windows and intranet environments, without requiring Docker, Java, Node.js, or external database servers.

The previous phases established:

- **Phase 0:** Technical feasibility, project bootstrap, and dependency validation.
- **Phase 1:** A functional Blazor search interface backed by Meilisearch.
- **Phase 2:** A normalized document model, indexing operations, Meilisearch index configuration, and integration tests.

**Your task is to introduce persistent application configuration, a conventional administration interface, basic system monitoring, and secure administrator authentication.**

This phase establishes the management foundation for future crawler configuration and execution features.

Do not implement features from subsequent phases.

---

## 1. Review the Existing Implementation

Before modifying the repository:

1. Read `roadmap.md` and existing architecture documentation.
2. Inspect the solution and project structure.
3. Review the Phase 1 search UI and navigation.
4. Review the Phase 2 search and indexing services.
5. Examine existing dependency injection registrations.
6. Review current Meilisearch connection configuration.
7. Review application startup and logging.
8. Identify any existing database or authentication infrastructure.
9. Run existing builds and tests to establish a baseline.

Preserve completed functionality.

Reuse existing services and abstractions wherever possible.

Avoid unnecessary architectural refactoring.

Do not introduce duplicate configuration systems or database contexts.

## 2. Phase Objectives

Implement the following:

1. SQLite persistence using Entity Framework Core.
2. Initial application settings and crawler configuration entities.
3. EF Core database migrations.
4. Administration layout and navigation.
5. Administration overview dashboard.
6. Meilisearch connection and health monitoring.
7. Indexed document count and basic system information.
8. Application and search configuration screens.
9. Initial administrator authentication.
10. Authorization boundaries between search and administration routes.
11. Secure handling of credentials and other secrets.
12. Unit and integration tests.

The administration interface must operate entirely through the existing Blazor application.

Do not introduce a separate frontend application.

## 3. SQLite Persistence

Introduce SQLite as Lantern's persistent metadata and configuration database.

Use:

- Entity Framework Core.
- The official EF Core SQLite provider.
- EF Core migrations.
- ASP.NET Core dependency injection.

SQLite is responsible for application metadata and operational state.

Meilisearch remains responsible for full-text search indexes and searchable document data.

### Database responsibilities

SQLite will eventually store:

- Application settings.
- Crawler definitions.
- Crawler execution history.
- Crawl synchronization state.
- Scheduler state.

However, Phase 3 must implement only the persistence structures necessary for the current requirements.

Do not implement the crawler job framework or scheduling infrastructure yet.

### Database location

Use a configurable database path.

For Windows deployment, prefer an application data directory outside the installation directory.

The eventual production layout may resemble:

```text
%ProgramData%\Lantern\
    database/
        lantern.db
    config/
    meilisearch/
    logs/
```

Do not hardcode the installation directory as the writable data directory.

During development, allow a convenient local database location.

Ensure that required directories are created safely.

### DbContext

Introduce a dedicated application DbContext, following repository conventions.

For example:

```csharp
public sealed class LanternDbContext : DbContext
{
    public LanternDbContext(
        DbContextOptions<LanternDbContext> options)
        : base(options)
    {
    }

    public DbSet<ApplicationSetting> ApplicationSettings
        => Set<ApplicationSetting>();

    public DbSet<CrawlerDefinition> CrawlerDefinitions
        => Set<CrawlerDefinition>();
}
```

These types and signatures are illustrative.

Adapt them to existing conventions.

Use appropriate entity constraints, indexes, and relationships.

Avoid introducing a generic repository abstraction unless there is a concrete need.

### Database lifetime

- Respect EF Core scoped DbContext lifetimes.
- Avoid sharing a DbContext between concurrent Blazor operations.
- Consider `IDbContextFactory<TContext>` for Interactive Server components and longer-lived services.
- Use asynchronous database operations where practical.
- Respect cancellation tokens.

## 4. Application Settings Model

Implement a minimal persistent application settings model.

Settings may include:

- Application display name.
- Search index configuration.
- Search result pagination defaults.
- Selected nonsecret application preferences.

Do not introduce settings without a current use case.

### Requirements

1. Persist settings in SQLite.
2. Validate settings before saving.
3. Load settings when needed.
4. Use documented defaults when settings are absent.
5. Preserve settings across application restarts.
6. Provide a service responsible for reading and updating settings.
7. Prevent invalid settings from corrupting application configuration.

### Configuration precedence

Define a clear separation between:

- Deployment configuration.
- Environment variables.
- Sensitive connection credentials.
- Administrator-editable application settings.

Do not silently combine conflicting configuration sources.

Document how settings are resolved and which configuration source owns each value.

### Sensitive configuration

Do not store Meilisearch API keys, administrator passwords, or other credentials as plaintext application settings in SQLite.

Use an appropriate server-side secret storage mechanism.

Do not return secrets to the browser simply to populate a settings form.

A configured secret may be displayed as:

```text
Meilisearch API Key: Configured
```

Do not display the actual value.

## 5. Crawler Configuration Persistence

Introduce an initial crawler configuration entity.

This entity establishes the persistence model needed by future phases.

Possible fields include:

```text
Id
Name
CrawlerType
Enabled
CreatedAt
UpdatedAt
```

Use stable identifiers and UTC timestamps.

### Requirements

- Persist crawler definitions in SQLite.
- Support the basic distinction between file and web crawler types if appropriate.
- Add validation for required fields.
- Establish a straightforward path for future crawler-specific configuration.
- Avoid storing credentials directly in crawler entities.

Do not introduce a large generic crawler framework.

### Important scope boundary

Phase 3 does not implement:

- Directory enumeration.
- Web crawling.
- Document extraction.
- Crawler execution.
- Crawl schedules.
- Job history.
- Incremental synchronization.
- Crawler creation and editing workflows.

These features belong to later phases.

Only introduce the minimal persistence foundation needed for future work.

## 6. Database Migrations

Use tracked EF Core migrations.

### Requirements

1. Create the initial database schema.
2. Add an initial migration.
3. Support creating a fresh SQLite database.
4. Preserve existing configuration data during future migrations.
5. Report migration failures clearly.
6. Document how to apply migrations.

Do not use `EnsureCreated()` as a replacement for migrations in the production application.

For development, provide a straightforward database initialization process.

If automatic migration at startup is implemented, ensure it is safe and does not silently ignore migration failures.

Avoid destructive database recreation.

Do not delete the existing SQLite database to resolve ordinary schema errors.

### Validation

Verify that:

- A new database can be initialized.
- The initial migration applies successfully.
- Settings survive application restart.
- Running initialization repeatedly does not erase existing data.

## 7. Administration UI Structure

Implement the administration interface inside the existing Blazor application.

Use a conventional management UI.

### Required navigation

```text
Administration
    |
    +-- Overview
    |
    +-- Crawlers
    |
    +-- Jobs
    |
    +-- Search Index
    |
    +-- Settings
    |
    +-- System Status
```

Use a dedicated administration layout if appropriate.

### UI principles

- Keep navigation clear and predictable.
- Use standard forms and tables.
- Prefer simple components over custom UI frameworks.
- Avoid unnecessary animations.
- Avoid decorative dashboards.
- Display useful operational information.
- Make errors visible and understandable.
- Support standard desktop browsers.
- Keep layout and terminology consistent.

Reuse the styling and component conventions established in Phase 1.

Do not redesign the public search interface.

### Route structure

Suggested routes:

```text
/search

/admin
/admin/crawlers
/admin/jobs
/admin/index
/admin/settings
/admin/system
```

Use repository conventions where existing routes differ.

All `/admin` routes must require administrator authorization.

The `/search` route must remain accessible according to the existing Phase 1 access model.

Do not accidentally require administrator privileges for ordinary search operations.

## 8. Administration Overview

Implement a simple overview dashboard.

The dashboard should display:

- Lantern application status.
- Meilisearch connection status.
- Active search index name.
- Indexed document count.
- Basic application information.
- Links to administration sections.

### Suggested layout

```text
Lantern Administration

Overview

--------------------------------------
Search Engine
Status: Connected
--------------------------------------

Search Index
Index: lantern-documents
Documents: 1,250
--------------------------------------

Application
Version: 0.x
Database: Available
--------------------------------------
```

These values are illustrative.

Display values retrieved from real services.

Do not use hardcoded production status values.

### Error handling

If Meilisearch is unavailable:

- Show an understandable error state.
- Keep the administration interface usable where possible.
- Do not crash the entire application.
- Do not expose API keys or sensitive connection details.

Do not treat a failed health check as permission to delete or recreate an existing index.

## 9. Meilisearch Health Monitoring

Reuse the existing Meilisearch client and configuration services from Phase 2.

Implement a lightweight health monitoring service.

### Required information

- Connection availability.
- Search engine health.
- Active index name.
- Indexed document count.
- Error details suitable for administrators.

### Requirements

1. Use Meilisearch's supported health and index APIs.
2. Handle connection failures.
3. Handle timeout conditions.
4. Respect cancellation.
5. Avoid unnecessary repeated requests.
6. Keep credentials server-side.
7. Distinguish unavailable services from an empty index.

Consider short-lived caching if it reduces redundant requests.

Do not implement a full monitoring or telemetry platform.

### Search index page

The `/admin/index` page should display basic index information.

Avoid destructive index management operations in this phase.

Do not expose unrestricted document deletion or index rebuilding controls through the administration UI unless necessary for a current Phase 3 requirement.

## 10. System Status Page

Implement a basic system information page.

Display useful information such as:

- Lantern version.
- Application environment.
- Application uptime, if readily available.
- SQLite database availability.
- Meilisearch availability.
- Active search index.
- Runtime version.

Only expose information suitable for administrators.

Do not display:

- API keys.
- Administrator password hashes.
- Connection secrets.
- Environment variable values containing credentials.
- Sensitive filesystem paths unnecessarily.

The page should remain functional when Meilisearch is offline.

Do not implement advanced process monitoring, performance history, or a diagnostics database.

## 11. Application Settings UI

Implement a settings page allowing administrators to view and modify supported configuration.

### Requirements

- Load current settings.
- Display editable nonsecret settings.
- Validate input.
- Save changes to SQLite.
- Display success and failure feedback.
- Prevent invalid changes from being persisted.
- Preserve unrelated settings.
- Clearly distinguish read-only deployment configuration from editable settings.

### Meilisearch connection settings

The interface may display:

- Configured endpoint.
- Active index name.
- Connection status.
- Whether authentication credentials are configured.

If editable connection settings are supported, ensure the existing server-side connection service can apply changes safely.

Do not save a setting that appears successful in the UI but cannot actually affect the running application.

For settings requiring an application restart, clearly explain that behavior.

Do not automatically reveal or overwrite existing credentials when an administrator saves unrelated settings.

### Test Connection

Provide a simple connection test action when practical.

The action must:

- Run on the server.
- Use the configured Meilisearch connection.
- Report success or failure.
- Avoid logging secret values.

Do not expose the Meilisearch master API key to the browser.

## 12. Administrator Authentication

Implement a minimal but secure administrator authentication mechanism.

Phase 3 requires one administrator identity.

Do not implement a complete multi-user management system.

### Recommended approach

Use standard ASP.NET Core authentication and authorization facilities.

Prefer cookie-based authentication for the Blazor administration interface.

Use a secure password hashing mechanism supplied by the .NET ecosystem.

Do not implement custom password hashing or authentication cryptography.

### Administrator provisioning

Provide a controlled mechanism for establishing the initial administrator identity.

Requirements:

- Do not ship a hardcoded default administrator password.
- Do not automatically create an account with publicly known credentials.
- Support secure initial password provisioning.
- Prevent unauthenticated users from taking over an already initialized installation.
- Document the administrator setup process.

Use a server-side provisioning mechanism appropriate for development and eventual Windows deployment.

### Login

Implement a login page.

For example:

```text
Lantern Administration

Administrator Login

Username
[                         ]

Password
[                         ]

[ Sign In ]
```

The login process must:

1. Validate credentials on the server.
2. Establish an authenticated session.
3. Redirect the administrator to the administration area.
4. Show a generic error for invalid credentials.
5. Avoid revealing whether individual credentials exist.
6. Avoid logging passwords.

Implement logout.

Terminate the authenticated session correctly.

### Session security

Use appropriate cookie settings.

Consider:

- HttpOnly cookies.
- Secure cookies when HTTPS is used.
- Appropriate SameSite behavior.
- Session expiration.
- Antiforgery protection.
- Protection against login abuse.

Use ASP.NET Core Data Protection for authentication cookie protection.

For deployment configurations requiring persistent authentication keys, provide a documented server-side key storage location and protect it appropriately.

Do not weaken cookie security merely to simplify development.

## 13. Authorization Boundaries

Authentication alone is not sufficient.

Implement server-side authorization for administration functionality.

### Requirements

- All administration pages require authentication.
- Administration endpoints require administrator authorization.
- Unauthorized users cannot modify application settings.
- Unauthorized users cannot access administrative diagnostics.
- Unauthorized users cannot invoke privileged operations directly.
- Logout terminates access to protected operations.
- The public search page remains available according to the project's current access model.

Use ASP.NET Core authorization policies or equivalent built-in mechanisms.

### Important Blazor consideration

Do not rely solely on hiding navigation links or conditional UI rendering.

Protected operations must enforce authorization on the server.

Ensure that interactive component actions and associated services cannot bypass authorization boundaries.

Apply appropriate antiforgery protection to state-changing HTTP requests.

Do not expose administration services through unprotected API endpoints.

## 14. Crawlers and Jobs Pages

Create basic administration navigation destinations for:

```text
/admin/crawlers
/admin/jobs
```

These pages are preparation for later phases.

### Crawlers page

The page may display:

- A clear explanation that crawler management is not yet implemented.
- Existing persisted crawler definitions, if useful and supported by the minimal schema.

Do not implement crawler creation or editing forms yet.

Full crawler configuration management belongs to Phase 6.

### Jobs page

The page may display a simple informational empty state.

For example:

```text
Crawler execution management is not
available in this phase.
```

Do not implement:

- Job scheduling.
- Manual crawler execution.
- Execution queues.
- Retry logic.
- Job cancellation.
- Persistent job history.

These features belong to Phase 7.

Avoid introducing fake operational metrics or simulated jobs.

## 15. Security Requirements

Phase 3 establishes the first administrative security boundary.

Apply secure defaults.

### Credentials and secrets

- Never include Meilisearch master API keys in rendered HTML.
- Never send administrator password hashes to clients.
- Never log passwords or API keys.
- Never store plaintext administrator passwords.
- Never return complete configuration objects containing secrets to UI components.
- Use server-side secret storage.

### Input validation

Validate all administrator-editable settings on the server.

Reject invalid configuration values.

Avoid exposing raw exception details to ordinary users.

### Database safety

- Use EF Core parameterized operations.
- Avoid dynamically constructed SQL from user input.
- Do not expose the SQLite database through a web-accessible directory.
- Avoid granting unnecessary filesystem permissions.

### Authentication safety

- Use built-in .NET authentication primitives.
- Protect state-changing requests.
- Prevent session fixation where applicable.
- Avoid open redirects after login.
- Ensure administrator-only services cannot be invoked anonymously.

### Logging

Log meaningful application errors.

Avoid logging sensitive data.

Use structured logging through the existing logging infrastructure.

Do not introduce an external logging service.

## 16. Configuration and Dependency Injection

Integrate new services into the existing application startup.

Register the necessary components, such as:

```text
LanternDbContext / DbContextFactory
ApplicationSettingsService
AdministrationStatusService
Authentication Services
Authorization Policies
Meilisearch Health Service
```

These names are illustrative.

Follow existing conventions.

### Requirements

- Keep database and service lifetimes correct.
- Avoid service locator patterns.
- Avoid static mutable global configuration.
- Prevent circular service dependencies.
- Use dependency injection for testability.
- Do not duplicate the existing Meilisearch client unnecessarily.

Ensure application startup is predictable.

A temporary Meilisearch outage should not prevent administrators from accessing database-backed configuration screens, unless a specific operation genuinely requires Meilisearch.

Database initialization failures must produce actionable diagnostics.

## 17. Testing Requirements

Add automated tests for the new persistence, administration, and security functionality.

### Unit tests

Cover at least:

1. Application setting validation.
2. Default setting resolution.
3. Invalid configuration rejection.
4. Crawler definition validation.
5. Meilisearch health response handling.
6. Meilisearch connection failure handling.
7. Authentication validation behavior.
8. Administrator authorization policies.

### SQLite integration tests

Use an actual SQLite database, preferably isolated per test.

Cover:

1. Database creation.
2. Initial migration application.
3. Application settings insertion.
4. Application settings updates.
5. Settings persistence across separate DbContext instances.
6. Crawler definition persistence.
7. Database constraint validation.
8. Repeated initialization without data loss.

Avoid relying solely on EF Core's in-memory provider for SQLite-specific behavior.

### Authentication and authorization tests

Verify:

1. Anonymous access to administration pages is denied.
2. A valid administrator can access administration pages.
3. Invalid credentials are rejected.
4. Logout invalidates the authenticated session.
5. Unauthorized settings modifications are rejected.
6. Direct requests to protected endpoints enforce authorization.
7. Public search functionality remains accessible under the intended access model.

Use meaningful HTTP-level or application integration tests where practical.

### Meilisearch integration tests

Where a real Meilisearch instance is available, verify:

- Healthy connection reporting.
- Active index information retrieval.
- Actual indexed document count.
- Unavailable engine handling.

Do not replace all integration tests with mocks.

Use isolated test resources.

### Regression tests

Run existing Phase 0–2 tests.

Verify that the search UI and indexing operations remain functional.

Do not claim tests passed unless they were actually executed.

## 18. Architecture Guidelines

Preserve the established project structure.

Suggested logical responsibilities:

```text
Lantern.Core
    |
    +-- Application setting models
    +-- Crawler definition models
    +-- Administration service contracts
    |
Lantern.Infrastructure
    |
    +-- SQLite persistence
    +-- EF Core migrations
    +-- Application settings service
    +-- Meilisearch health integration
    |
Lantern.Web
    |
    +-- Administration layout
    +-- Administration pages
    +-- Login / Logout
    +-- Authentication
    +-- Authorization
    +-- Configuration
```

Adapt these responsibilities to the existing repository.

Do not create new projects unless justified by the current architecture.

### Engineering principles

1. Prefer built-in .NET functionality.
2. Keep abstractions minimal.
3. Reuse existing components.
4. Keep persistence independent of UI concerns.
5. Avoid unnecessary third-party dependencies.
6. Maintain the separation between SQLite and Meilisearch.
7. Preserve compatibility with completed phases.
8. Support eventual native Windows deployment.

## 19. Out of Scope

Do not implement:

- Document extraction.
- File crawlers.
- Web crawlers.
- Crawler configuration forms.
- Crawler execution.
- Job scheduling.
- Job history tracking.
- File synchronization state.
- Full user management.
- Multi-tenant access control.
- Windows Integrated Authentication.
- Active Directory integration.
- Per-document access control.
- Advanced monitoring dashboards.
- Notification systems.
- Distributed background jobs.
- Search UI redesign.
- Windows service installation.
- Release packaging.

Do not introduce these features merely because supporting entities or navigation pages are being created.

## 20. Acceptance Criteria

Phase 3 is complete only when all required conditions are satisfied.

### Persistence

- SQLite is integrated through EF Core.
- Initial migrations are created and tested.
- Application configuration survives restart.
- Basic crawler definitions can be persisted.
- Schema upgrades use tracked migrations.
- Existing data is not destroyed during routine initialization.

### Administration UI

- Administration navigation is implemented.
- Overview page displays real system information.
- Search index information is visible.
- Meilisearch health status is visible.
- Settings can be viewed and modified where supported.
- Administrative errors are handled gracefully.

### Authentication

- An administrator identity can be securely provisioned.
- Login and logout function correctly.
- Administration pages require authorization.
- Unauthorized settings changes are rejected.
- Administrative operations enforce server-side authorization.
- No master API key is delivered to the browser.

### Compatibility

- Phase 1 search UI remains functional.
- Phase 2 indexing services remain functional.
- Existing routes and core functionality are preserved.
- No new external infrastructure is required.

### Testing

- `dotnet build` succeeds.
- Existing tests pass.
- New unit tests pass.
- SQLite integration tests pass.
- Authentication and authorization tests pass.
- Meilisearch health integration is verified where available.

Any unverified acceptance criteria must be identified in the completion report.

## 21. Deliverables

Provide:

1. EF Core SQLite integration.
2. Initial DbContext and entity configurations.
3. Initial EF Core migration.
4. Application settings service.
5. Minimal crawler definition persistence.
6. Administration layout and navigation.
7. Administration overview page.
8. Search index information page.
9. Settings page.
10. System status page.
11. Basic Crawlers and Jobs navigation pages.
12. Administrator login and logout.
13. Authentication and authorization configuration.
14. Meilisearch health monitoring.
15. Unit and integration tests.
16. Updated documentation.

Document:

- Database initialization.
- Database location.
- Migration commands.
- Initial administrator provisioning.
- Authentication configuration.
- Secret storage.
- Supported administration features.
- Current limitations.

## 22. Required Completion Report

At the end of implementation, provide the following.

### A. Implementation summary

Describe the completed functionality.

### B. Files changed

List all important files created or modified.

### C. Architectural decisions

Explain:

- SQLite database location.
- Entity and migration design.
- Application configuration strategy.
- Administrator credential provisioning.
- Authentication and authorization model.
- Secret storage mechanism.
- Meilisearch health integration.

### D. Build and test commands

List the commands actually executed.

### E. Test results

Clearly distinguish:

- Passed tests.
- Failed tests.
- Tests not executed.

### F. Known limitations

Describe remaining limitations, especially:

- Authentication restrictions.
- Deployment configuration requirements.
- Database lifecycle concerns.
- Deferred crawler administration functionality.

### G. Remaining work

Identify incomplete requirements and their impact.

Do not declare Phase 3 complete unless its acceptance criteria have been satisfied or clearly identify which remain incomplete.

---

## Final Instructions

Implement Phase 3 directly in the existing Lantern repository.

Inspect the current implementation before editing code.

Preserve all completed Phase 0, Phase 1, and Phase 2 functionality.

Use SQLite exclusively for application metadata and configuration, not as a replacement for Meilisearch.

Use standard ASP.NET Core authentication and authorization facilities.

Make administrator-only operations secure by default.

Keep the interface conventional, minimal, and understandable.

Avoid placeholder production functionality, fake health information, and unverified test claims.

**The primary success condition is a secure, persistent administration foundation that future crawler configuration and job management features can build upon without requiring a redesign of settings, authentication, navigation, or persistence.**

Do not implement Phase 4 or later features.

Do not begin the next phase automatically.
