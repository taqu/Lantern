# Lantern — Phase 6 Implementation Instructions
## Crawler Configuration and Administration UI

You are implementing **Phase 6: Crawler Configuration UI** of the Lantern project.

Lantern is a lightweight, self-hosted full-text search platform designed for Windows and intranet environments.

The application uses:

- .NET 10 and ASP.NET Core.
- Blazor Web App with Interactive Server.
- Meilisearch Community Edition.
- SQLite with Entity Framework Core.
- LlamaPure and IBM Granite Embedding 97M Multilingual R2.
- Local REST-based embedding and hybrid search.
- Document extraction and structure-aware chunking.
- Incremental local file crawling.

Phases 0–5 have established the search interface, indexing infrastructure, administration foundation, hybrid search, document extraction, and local file crawler.

**Your task is to implement a browser-based administration interface for creating, viewing, editing, deleting, testing, enabling, disabling, and manually executing file crawler configurations.**

Reuse the existing Phase 5 crawler implementation.

Do not introduce the persistent job scheduling or execution management infrastructure planned for Phase 7.

---

## 1. Review the Existing Implementation

Before modifying the repository:

1. Read `roadmap.md` and existing architecture documentation.
2. Inspect the current solution structure.
3. Review the Phase 3 administration layout and authentication.
4. Review the existing SQLite crawler configuration entities.
5. Review Phase 5 file crawler configuration and execution services.
6. Review filesystem path validation and filtering logic.
7. Review document extraction format detection.
8. Review crawler state persistence and deletion reconciliation.
9. Review existing application settings and dependency injection.
10. Run the existing build and tests to establish a baseline.

The actual repository is the source of truth.

Reuse established models and services.

Avoid duplicating crawler configuration or validation logic.

Preserve all existing functionality.

## 2. Phase Objectives

Implement:

1. Crawler list page.
2. File crawler creation form.
3. File crawler editing form.
4. Crawler deletion.
5. Enabled/disabled state management.
6. Server-side configuration validation.
7. Filesystem path-access testing.
8. Configurable file inclusion and exclusion rules.
9. Safe configuration persistence in SQLite.
10. Manual crawler execution.
11. Basic execution feedback.
12. Administrative authorization.
13. Unit and integration tests.
14. Updated administration documentation.

The entire workflow must be available through the existing Blazor application.

Do not require administrators to edit JSON configuration files manually.

## 3. Administration Navigation

Reuse the existing administration layout.

The crawler management interface should be accessible through:

```text
/admin/crawlers
```

Suggested routes:

```text
/admin/crawlers
/admin/crawlers/create
/admin/crawlers/{id}/edit
/admin/crawlers/{id}
```

Follow established routing conventions if different.

### UI principles

- Use conventional administration UI patterns.
- Keep forms understandable.
- Display useful validation messages.
- Avoid unnecessary animations and decorative components.
- Preserve the existing application styling.
- Support keyboard navigation.
- Use accessible labels.
- Keep page behavior predictable.
- Make destructive actions explicit.

Do not redesign the search interface or other administration sections.

## 4. Crawler List Page

Implement a crawler list view.

Suggested columns:

| Column | Description |
|---|---|
| Name | Configured crawler name |
| Type | File |
| Root Path | Local or UNC directory |
| Enabled | Enabled or disabled |
| Last Run | Most recent available execution time |
| Last Result | Most recent available result |
| Actions | Edit, Test, Run, Delete |

Only display information supported by the existing persistence layer.

Do not fabricate execution history.

If Phase 5 does not persist last-run details, display an appropriate unavailable state.

### Required actions

- Create crawler.
- View crawler details.
- Edit crawler.
- Enable or disable crawler.
- Test directory access.
- Run crawler manually.
- Delete crawler.

### Empty state

When no crawlers exist, display an understandable message and a clear creation action.

Do not create fake default crawlers.

## 5. File Crawler Creation Form

Implement a conventional form for creating a file crawler.

### Suggested UI

```text
Create File Crawler

Name
[ Company Documents                    ]

Root Directory
[ \\fileserver\shared\documents         ]

[✓] Include subdirectories

File Types
(•) All supported file types
( ) Only selected extensions

Included Extensions
[ .pdf,.docx,.pptx,.xlsx,.txt,.cpp     ]

Excluded Extensions
[ .exe,.dll                            ]

Excluded Directories
[ .git,node_modules,bin,obj            ]

Maximum File Size
[ 100 ] MB

Maximum Concurrent Files
[ 2 ]

[ Test Path ]    [ Cancel ]    [ Save ]
```

This layout is illustrative.

Adapt it to existing application conventions.

### Required fields

- Crawler name.
- Root directory.
- Recursive enumeration.
- Included file extensions or supported-format mode.
- Excluded extensions.
- Excluded directories.
- Maximum file size.
- Maximum concurrent file processing.
- Enabled state.

Avoid exposing advanced settings that the Phase 5 crawler cannot actually apply.

## 6. General Text and Source Code Support

Phase 4 supports general-purpose text extraction, including source code and configuration files.

The crawler UI must not accidentally restrict discovery to PDF and Office documents.

### Supported file categories

Include:

- PDF documents.
- Microsoft Office documents.
- Plain text and Markdown.
- HTML.
- Programming source code.
- Configuration files.
- Logs.
- Structured text such as JSON, XML, YAML, and TOML.
- Extensionless text files when supported by the extraction pipeline.

### File type selection

Prefer two modes:

**All supported file types**

Use the Phase 4 format detection capabilities.

Avoid maintaining a separate hardcoded extension whitelist in the UI.

**Selected extensions**

Allow administrators to define included extensions.

Normalize extensions consistently.

Support exclusion rules that take precedence over inclusion rules.

### Extensionless files

Do not automatically reject extensionless text files.

Use the existing text/binary detection mechanism.

### Binary files

Binary files must not be interpreted as general text indiscriminately.

Do not expose unsupported formats as successfully indexable.

### Configuration

Preserve Phase 4 text encoding behavior.

Do not add a competing encoding detection implementation in the crawler UI.

If the extraction pipeline supports an explicit fallback encoding, expose it only when there is a clear current requirement.

## 7. Filesystem Path Configuration

Support:

```text
C:\Documents
D:\Shared\Reports
\\fileserver\shared\documents
```

### Requirements

- Accept absolute Windows paths.
- Accept UNC paths.
- Reject malformed paths.
- Reject empty paths.
- Reject unsupported relative paths.
- Reject filesystem roots outside approved boundaries.
- Use the existing path normalization service.
- Preserve readable paths in the UI.
- Avoid inconsistent identity changes caused by equivalent path representations.

### Important security rule

Filesystem validation must occur on the server.

Do not rely on client-side validation alone.

### Root path changes

Changing a crawler root may affect document identity and deletion reconciliation.

Do not silently treat an entirely different root directory as an ordinary harmless configuration edit.

Implement a safe policy for root changes.

Prefer requiring an explicit confirmation, or treating root relocation as a new crawler identity if the existing synchronization architecture cannot safely migrate the state.

Do not automatically delete all previously indexed documents when a root path changes.

Document the chosen behavior.

## 8. Directory Exclusion Rules

Allow administrators to configure excluded directories.

Example:

```text
.git
node_modules
bin
obj
.vs
```

### Requirements

- Support multiple exclusion entries.
- Normalize entries consistently.
- Reject invalid patterns.
- Avoid ambiguous path matching.
- Apply exclusion rules before descending into directories.
- Reuse Phase 5 filtering behavior.
- Prevent exclusions from escaping configured root boundaries.

If the crawler supports full relative-path exclusions, preserve that functionality.

Do not introduce regular expressions or complex glob syntax unless already supported by the crawler implementation.

### Configuration changes

Changing exclusion rules may make previously indexed documents no longer eligible.

Do not silently remove their indexed data during configuration save.

Apply changes during an explicit crawl using safe reconciliation semantics.

## 9. Crawler Configuration Persistence

Use the existing Phase 3 SQLite database.

Extend the current crawler configuration entities only where necessary.

### Requirements

- Persist crawler definitions.
- Persist enabled state.
- Persist inclusion and exclusion rules.
- Persist file-size and concurrency limits.
- Preserve stable crawler IDs.
- Preserve creation and modification timestamps.
- Use EF Core migrations for schema changes.
- Ensure configuration survives application restarts.

### Validation

Validate settings before saving.

Reject invalid configurations.

Avoid partially persisted configurations.

Use appropriate database constraints.

### Concurrency

Prevent accidental overwriting of another administrator's changes where practical.

Consider optimistic concurrency using a version or concurrency token if the existing persistence model supports it.

At minimum, handle deleted or changed records gracefully rather than crashing.

## 10. Crawler Editing

Implement an editing form using the same validation and presentation conventions as crawler creation.

### Requirements

- Load existing configuration.
- Allow supported fields to be modified.
- Preserve crawler identity.
- Save changes without restarting Lantern.
- Show successful save feedback.
- Show validation errors.
- Prevent accidental loss of unrelated settings.

### Editing an active crawler

Do not allow unsafe mutation of the configuration currently used by an active crawl.

Prefer taking an immutable configuration snapshot when a crawl begins.

Changes saved during execution should apply to subsequent runs.

If the existing crawler architecture cannot support this safely, reject edits while the crawler is running and display a clear explanation.

## 11. Enabled and Disabled State

Allow administrators to enable or disable crawler definitions.

### Requirements

- Persist the enabled state.
- Display the current state clearly.
- Prevent new manual execution of disabled crawlers.
- Do not automatically cancel an active crawl merely because its configuration was disabled.
- Do not delete indexed documents when disabling a crawler.

Disabling a crawler must not imply removing its search index.

### Important distinction

A disabled crawler means it should not start new work.

It does not mean previously indexed content must disappear.

## 12. Test Path Functionality

Implement a **Test Path** operation.

This feature must verify filesystem access from the Lantern server process.

### Required checks

1. Validate path syntax.
2. Verify the root directory exists.
3. Verify the application identity can access it.
4. Attempt bounded directory enumeration.
5. Report a clear success or failure result.
6. Respect cancellation and timeouts where possible.

### Example results

```text
Connection successful.
Directory is accessible.
```

Or:

```text
Connection failed.
Access denied.
```

Or:

```text
Connection failed.
Network path unavailable.
```

### Important constraints

- Do not recursively scan the entire directory during a path test.
- Do not extract documents.
- Do not create embeddings.
- Do not modify files.
- Do not modify the search index.
- Do not update crawl synchronization state.

The path test must be read-only.

### UNC behavior

Use the Lantern process or service identity.

Do not assume the administrator's browser credentials grant the server access to the network share.

Document this distinction.

### Path safety

Do not allow the path test to become an arbitrary filesystem exploration tool.

Enforce administrator authorization and configured filesystem boundaries.

Do not reveal unnecessary details about inaccessible paths.

## 13. Manual Crawler Execution

Allow administrators to execute an existing crawler manually.

Reuse the Phase 5 single-run crawler service.

Do not duplicate the crawler implementation.

### Required behavior

1. Verify administrator authorization.
2. Load the saved crawler configuration.
3. Validate its enabled state.
4. Validate the effective configuration.
5. Prevent overlapping execution of the same crawler.
6. Start one crawl using the existing execution service.
7. Display meaningful execution feedback.
8. Allow the administrator to continue using the UI.
9. Report completion or failure.

### Critical Blazor requirement

Do not execute a long-running crawl directly inside a Blazor component event handler.

The event handler must not block until the crawl finishes.

Use a minimal application-owned execution coordinator that accepts a single-run request and executes it outside the component lifecycle.

### Scope boundary

This coordinator must remain lightweight.

It may maintain in-memory state for currently active manual executions.

Do not implement:

- Persistent job queues.
- Recurring schedules.
- Retry policies.
- Cron expressions.
- Full execution history.
- Complex background job orchestration.

These belong to Phase 7.

### Execution feedback

Display available information such as:

```text
Crawler: Company Documents
Status: Running

Processed: 125
Indexed: 42
Unchanged: 78
Failed: 5
```

Only display counters actually provided by the crawler.

Do not fabricate progress percentages when the total number of files is unknown.

### Application shutdown

Manual execution must respect application shutdown.

Do not leave unobserved background tasks.

Do not allow an interrupted crawl to perform unsafe deletion reconciliation.

## 14. Prevent Overlapping Execution

Only one execution of a given crawler should run at a time.

### Requirements

- Reject duplicate start requests.
- Handle repeated button clicks.
- Maintain an active-execution registry.
- Release execution state after completion or failure.
- Prevent active executions from using mutable configuration objects.
- Use a concurrency-safe mechanism.

Do not rely only on disabling the Run button.

Server-side enforcement is required.

### Multi-instance limitation

Lantern currently targets a single application instance.

An in-memory per-crawler execution lock is acceptable for Phase 6.

Document that cross-process execution coordination is not yet implemented.

## 15. Crawler Deletion

Implement deletion of crawler definitions.

### Requirements

- Require administrator authorization.
- Show a confirmation dialog.
- Identify the crawler being deleted.
- Explain consequences.
- Prevent deletion while the crawler is actively running, unless a safe supported workflow exists.
- Avoid silently deleting indexed documents.
- Handle database errors gracefully.

### Indexed document retention

Deleting a crawler definition and deleting all documents indexed by that crawler are separate operations.

For Phase 6, prefer a conservative policy.

Do not perform automatic index deletion as an implicit side effect of removing configuration.

### State consistency

Crawler configuration, crawl state, and indexed documents may be related.

Define how existing crawl state is handled when a crawler definition is deleted.

Avoid orphaned state that cannot be recovered or cleaned up.

If complete cleanup cannot be implemented safely in this phase, prefer a soft-delete or archived configuration state with a documented recovery path.

Do not introduce complex cascade deletion behavior without understanding its effects on Meilisearch.

## 16. Administration Security

All crawler management features are administrator-only.

### Required authorization

Protect:

- Crawler list.
- Crawler details.
- Crawler creation.
- Crawler editing.
- Crawler deletion.
- Enable/disable operations.
- Test Path.
- Manual Run.

Enforce authorization on the server.

Hiding buttons is not sufficient.

### Input security

Validate:

- Crawler names.
- Filesystem paths.
- Extension lists.
- Directory exclusions.
- File-size limits.
- Concurrency limits.

Prevent path traversal and unauthorized filesystem access.

Do not permit crawler configuration to bypass established Phase 5 path restrictions.

### Request safety

Apply appropriate antiforgery protections to state-changing HTTP endpoints.

Avoid exposing unprotected administrative services.

Do not include sensitive credentials in UI responses or logs.

### HTML and rendering

Treat crawler names and paths as untrusted display values.

Use normal Blazor text rendering.

Do not render user-controlled strings as raw HTML.

## 17. Configuration Validation

Create or reuse one authoritative server-side validation service.

Use the same validation rules for:

- Create.
- Edit.
- Test Path.
- Manual Run.

### Validation requirements

- Name must be nonempty.
- Root path must be valid.
- Maximum file size must be positive and within supported limits.
- Maximum concurrency must be positive and bounded.
- Extension values must be normalized.
- Exclusion rules must be valid.
- Root boundaries must be respected.
- Unsupported configuration combinations must be rejected.

### Important distinction

Configuration validity and filesystem availability are not identical.

A valid UNC path may be temporarily unavailable.

Do not automatically delete or invalidate a previously saved crawler merely because its network share is temporarily disconnected.

Report availability separately.

## 18. User Experience and Error Handling

Use conventional Blazor forms and feedback.

### Required states

- Loading.
- Empty list.
- Validation error.
- Save success.
- Save failure.
- Path test running.
- Path test success.
- Path test failure.
- Crawler running.
- Crawler completed.
- Crawler failed.

Avoid showing raw exceptions to administrators.

Present actionable messages.

### Form behavior

- Preserve entered values after validation errors.
- Disable duplicate submissions while saving.
- Clearly identify required fields.
- Confirm destructive actions.
- Return to an appropriate page after successful operations.

Do not require full application restarts for configuration changes.

## 19. Testing Requirements

Implement unit, persistence, UI, and integration tests.

### Unit tests

Cover:

1. Crawler name validation.
2. Root path validation.
3. UNC path validation.
4. File extension normalization.
5. Included/excluded extension precedence.
6. Extensionless text support.
7. Directory exclusion validation.
8. File-size limit validation.
9. Concurrency limit validation.
10. Enabled/disabled state behavior.
11. Root path change safety.
12. Active crawler edit handling.
13. Overlapping execution prevention.
14. Deletion policy validation.

### SQLite integration tests

Use a real SQLite database.

Test:

1. Crawler creation.
2. Crawler editing.
3. Crawler deletion or archival.
4. Configuration persistence after restart.
5. Enabled state persistence.
6. Include/exclude rule persistence.
7. Migration application.
8. Invalid configuration rejection.
9. Stable crawler identity.
10. Existing crawl-state preservation.

### Path testing

Use deterministic temporary directories.

Test:

- Accessible directory.
- Missing directory.
- Invalid path.
- Permission failure where supported.
- Nonrecursive access test.
- UNC path handling.
- Canceled test.
- Path test not modifying index or crawl state.

Do not require an external network share for all automated tests.

### Administration tests

Verify:

1. Anonymous users cannot access crawler management.
2. Unauthorized users cannot create crawlers.
3. Unauthorized users cannot modify crawlers.
4. Unauthorized users cannot delete crawlers.
5. Unauthorized users cannot test filesystem paths.
6. Unauthorized users cannot manually run crawlers.
7. Valid administrators can perform supported operations.
8. Validation failures produce useful messages.

### Manual execution tests

Verify:

1. A saved crawler can be executed.
2. Disabled crawlers cannot be started.
3. Duplicate start requests are rejected.
4. Execution uses a configuration snapshot.
5. Completion status is reported.
6. Failures are reported.
7. Application shutdown cancels active executions safely.
8. A failed scan does not trigger unsafe index deletion.

### Regression tests

Run existing Phase 0–5 tests.

Ensure:

- Keyword search remains functional.
- Hybrid search remains functional.
- Document extraction remains functional.
- General text and source code extraction remain functional.
- UTF-8 handling remains correct.
- Incremental crawling remains functional.
- Administration authentication remains functional.

Do not claim that tests passed unless they actually executed successfully.

## 20. Architecture Guidelines

Preserve the existing project structure.

Suggested logical organization:

```text
Lantern.Core
    |
    +-- FileCrawlerConfiguration
    +-- Crawler validation contracts

Lantern.Crawling
    |
    +-- Existing Phase 5 crawler
    +-- Manual execution coordinator

Lantern.Infrastructure
    |
    +-- Crawler configuration persistence
    +-- SQLite migrations
    +-- Existing crawl-state persistence

Lantern.Web
    |
    +-- Admin/Crawlers
    |      +-- List
    |      +-- Create
    |      +-- Edit
    |      +-- Details
    |
    +-- Crawler form components
    +-- Path test UI
    +-- Manual execution UI
```

This is illustrative.

Adapt to the existing repository.

Prefer simple .NET services.

Avoid:

- Distributed job frameworks.
- External message brokers.
- Separate frontend applications.
- New JavaScript frameworks.
- Unnecessary third-party component libraries.
- Complex scheduler abstractions.
- Unrelated architectural refactoring.

## 21. Out of Scope

Do not implement:

- Web crawler configuration.
- HTTP crawling.
- Recurring crawler scheduling.
- Cron expressions.
- Persistent job queues.
- Full job history.
- Automatic retries.
- Advanced execution dashboards.
- Notification systems.
- Per-document Windows ACL synchronization.
- Windows Integrated Authentication.
- File system watchers.
- RAG.
- Chat interfaces.
- OCR.
- Distributed crawler execution.
- Windows service packaging.

These belong to future phases or deferred enhancements.

## 22. Acceptance Criteria

Phase 6 is complete only when all required conditions are satisfied.

### Crawler administration

- Administrators can view crawler definitions.
- Administrators can create file crawlers.
- Administrators can edit crawler configurations.
- Administrators can enable and disable crawlers.
- Administrators can delete or safely archive crawlers.
- Configurations persist in SQLite.
- Changes do not require application restarts.

### File type support

- PDF and Office document types are supported.
- General text and source code files can be configured.
- Extensionless text files are not unnecessarily excluded.
- Include/exclude rules work correctly.
- Existing Phase 4 text detection is reused.

### Path validation

- Local paths are supported.
- UNC paths are supported.
- Invalid and unsafe settings are rejected.
- Test Path reports actual server-side accessibility.
- Path tests do not modify crawler state or indexed data.

### Manual execution

- Saved crawlers can be executed manually.
- Disabled crawlers cannot start.
- Overlapping runs are prevented.
- Execution does not block Blazor UI interactions.
- Basic status and results are displayed.
- Failed executions are reported.

### Security

- All crawler administration actions require authorization.
- Direct service and endpoint access cannot bypass authorization.
- File paths are validated on the server.
- Destructive operations require explicit confirmation.
- Existing security boundaries remain intact.

### Testing

- The solution builds.
- Existing tests pass.
- New unit tests pass.
- SQLite integration tests pass.
- Authorization tests pass.
- Manual execution tests pass.

Clearly report unmet acceptance criteria.

## 23. Deliverables

Provide:

1. Crawler list page.
2. File crawler creation page.
3. File crawler editing page.
4. Crawler details page.
5. Enable/disable controls.
6. Safe deletion or archival workflow.
7. Server-side configuration validation.
8. Filesystem Test Path operation.
9. Manual crawler execution.
10. In-memory execution coordination.
11. Basic execution status reporting.
12. SQLite persistence updates.
13. Automated tests.
14. Updated documentation.

Document:

- How to create a crawler.
- How to configure file type filters.
- How to configure excluded directories.
- How to use UNC paths.
- How Test Path works.
- How to run a crawler manually.
- How crawler state is preserved.
- What happens when a crawler is disabled or deleted.
- Known limitations before Phase 7.

## 24. Required Completion Report

At the end of implementation, provide:

### A. Implementation summary

Describe implemented crawler administration functionality.

### B. Files changed

List created and modified files.

### C. Architectural decisions

Explain:

- Crawler configuration persistence.
- Validation strategy.
- Path-access testing.
- File type filtering.
- Safe root-path modifications.
- Manual execution coordination.
- Overlapping execution prevention.
- Deletion and retention policy.
- Authorization enforcement.

### D. Build and test commands

List commands actually executed.

### E. Test results

Clearly distinguish:

- Passed tests.
- Failed tests.
- Skipped tests.
- Tests not executed.

### F. Known limitations

Document remaining limitations, especially:

- Lack of persistent job history.
- Lack of scheduling.
- In-memory manual execution state.
- UNC permissions.
- Configuration changes during execution.
- Root path changes and index retention.

### G. Remaining work

Identify incomplete acceptance criteria.

Do not begin Phase 7 automatically.

---

## Final Instructions

Implement Phase 6 directly in the existing Lantern repository.

Inspect and preserve Phases 0–5.

Reuse the existing file crawler, document extraction, chunking, indexing, SQLite, and administration infrastructure.

Provide a straightforward browser-based workflow for creating, editing, testing, enabling, disabling, deleting, and manually running file crawlers.

Support PDF, Office documents, source code, configuration files, and general text using the existing extraction pipeline.

Do not implement duplicate file format detection or character encoding logic.

Keep filesystem operations server-side and enforce administrator authorization.

Use a lightweight execution coordinator for manual runs without introducing the full Phase 7 job framework.

Preserve indexed documents and crawl state during configuration errors, incomplete scans, and unsafe root changes.

**The primary success condition is that an administrator can configure a local or UNC file crawler entirely through the Lantern web interface, verify its accessibility, run it manually, and search the resulting documents without editing configuration files or restarting the application.**

Do not implement Phase 7 or later features automatically.
