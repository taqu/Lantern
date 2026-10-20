# Lantern — Phase 10 Implementation Instructions
## Security, Reliability, and Recovery Hardening

You are implementing **Phase 10: Security and Reliability** of the Lantern project.

Lantern is a lightweight, self-hosted full-text search platform designed primarily for Windows and intranet environments.

The application uses:

- .NET 10 and ASP.NET Core.
- Blazor Web App with Interactive Server.
- Meilisearch Community Edition.
- SQLite with Entity Framework Core.
- LlamaPure and IBM Granite Embedding 97M Multilingual R2.
- Local REST-based embedding and hybrid search.
- Document extraction and structure-aware chunking.
- Incremental file and web crawlers.
- Persistent crawler job execution and scheduling.
- Administrator-managed search categories.
- Document-oriented keyword and hybrid search.

Phases 0–9 have established the application's primary functionality.

**Your task is to audit, harden, test, and document the security and reliability of the existing Lantern implementation without introducing major new product features.**

This phase prepares Lantern for automated quality assurance and Windows release packaging in Phases 11 and 12.

Do not implement Phase 11 or later features.

---

## 1. Review the Existing Implementation

Before modifying the repository:

1. Read `roadmap.md` and existing architecture documentation.
2. Inspect the current solution and project structure.
3. Review authentication and authorization.
4. Review Blazor routes, components, and HTTP endpoints.
5. Review SQLite persistence and migrations.
6. Review Meilisearch configuration, API keys, and network binding.
7. Review the local embedding REST server.
8. Review file crawler root validation and filesystem access.
9. Review web crawler SSRF protection, redirect handling, and network restrictions.
10. Review document extraction and chunking resource limits.
11. Review persistent crawler jobs, scheduling, and crash recovery.
12. Review category assignment and index synchronization.
13. Review search input validation and source navigation.
14. Review application logging and secret handling.
15. Review backup and recovery capabilities.
16. Run existing builds and tests to establish a baseline.

The actual repository is the source of truth.

Do not assume earlier phase requirements were implemented correctly merely because those phases were reported complete.

Identify actual security and reliability gaps before changing code.

Preserve completed functionality.

Avoid unrelated refactoring.

## 2. Phase Objectives

Implement or verify:

1. Authentication and authorization hardening.
2. CSRF and input validation protection.
3. Secure session and credential handling.
4. Meilisearch network isolation.
5. Embedding REST server isolation.
6. Web crawler SSRF protection.
7. Filesystem crawler boundary enforcement.
8. Safe document extraction resource limits.
9. Search query and source navigation security.
10. Persistent job recovery.
11. SQLite and Meilisearch synchronization reliability.
12. Category synchronization recovery.
13. Backup and restoration procedures.
14. Secure application configuration.
15. Least-privilege operation.
16. Security and fault-injection tests.
17. Security boundary documentation.

Prioritize correctness, data preservation, and operational safety over new functionality.

## 3. Security Model and Trust Boundaries

Document the application's trust boundaries.

The expected architecture is:

```text
Intranet Browser
       |
       | HTTP / HTTPS
       v
ASP.NET Core / Blazor
       |
       +-- Authentication
       +-- Authorization
       +-- Search Service
       +-- Administration Service
       +-- Crawler Configuration
       +-- Job Management
       |
       +-----------------------------+
       |                             |
       v                             v
SQLite                       Meilisearch
Configuration                 Search Index
Crawler State                 Document Chunks
Jobs                          Vectors
Categories                    Search API
       |
       v
Crawler Services
       |
       +-- Approved File Roots
       |
       +-- Approved Web Targets
       |
       v
Document Extraction
       |
       v
Embedding REST Server
       |
       v
LlamaPure / GGUF Model
```

### Trust assumptions

Lantern is intended for a trusted but not necessarily isolated intranet.

Do not assume that every intranet client is trusted.

Do not assume that crawler targets contain safe content.

Do not assume that administrators always enter valid configuration.

### Security principles

- Expose only required HTTP endpoints.
- Enforce authorization server-side.
- Keep credentials inside trusted server components.
- Restrict filesystem and network access.
- Validate untrusted inputs.
- Bound expensive operations.
- Use least-privilege process identities.
- Preserve recoverability after failures.
- Avoid silently destructive operations.

### Existing access-control limitation

Lantern v1.0 does not implement per-document Windows ACL synchronization.

Do not claim that indexed search results enforce the original filesystem permissions.

Document the common-access search model and its implications.

Do not implement a complete document-level authorization system in this phase.

## 4. Authentication Hardening

Audit the existing Phase 3 administrator authentication implementation.

Use standard ASP.NET Core authentication facilities.

### Requirements

- Verify administrator credential validation.
- Verify secure password hashing.
- Verify secure initial administrator provisioning.
- Verify login and logout behavior.
- Verify session expiration.
- Verify authentication cookie configuration.
- Prevent session fixation where applicable.
- Prevent open redirects during authentication.
- Reject invalid authentication state.
- Avoid user enumeration through login error messages.

### Password storage

Do not store plaintext passwords.

Do not implement custom password hashing.

Use an established password hashing implementation.

### Login abuse

Apply reasonable protection against repeated failed authentication attempts.

Possible measures include:

- ASP.NET Core rate limiting.
- Account-level throttling.
- Temporary lockout where appropriate.

Do not implement a complex identity management platform.

### Session security

Verify appropriate use of:

```text
HttpOnly
Secure
SameSite
Session Expiration
Data Protection
```

Production deployments using HTTPS must not expose authentication cookies over insecure transport.

Document the supported authentication deployment model.

## 5. Authorization Audit

Audit every administrative route and operation.

### Protected operations

Include:

- Administration dashboard.
- Application settings.
- Meilisearch configuration.
- Embedding configuration.
- Category management.
- Crawler creation.
- Crawler editing.
- Crawler deletion.
- Filesystem path testing.
- Web connection testing.
- Job submission.
- Job cancellation.
- Job retry.
- Scheduling configuration.
- Execution history.
- Backup and restore operations.

### Requirements

- Enforce authorization in server-side operations.
- Protect direct HTTP endpoints.
- Protect privileged service entry points exposed through web requests.
- Prevent UI-only authorization.
- Avoid relying solely on hidden buttons.
- Verify unauthenticated and unauthorized behavior.
- Preserve intended public search access.

### Blazor considerations

Interactive Server components execute server-side operations.

Do not assume a component is secure merely because its page uses an authorization attribute.

Review event handlers and the services they invoke.

Ensure that privileged operations enforce appropriate authorization at their exposed entry points.

### Category security

Search categories are organizational filters.

They are not access-control boundaries.

Do not allow category configuration or URL parameters to bypass the established search access model.

## 6. CSRF and State-Changing Requests

Review ASP.NET Core antiforgery configuration.

### Requirements

- Verify antiforgery middleware and endpoint behavior.
- Protect cookie-authenticated state-changing operations.
- Review form submissions and administrative APIs.
- Validate HTTP methods.
- Avoid state changes through GET requests.
- Validate request origin where appropriate.
- Avoid globally disabling antiforgery protection.

### Operations to verify

- Login.
- Logout.
- Settings changes.
- Category changes.
- Crawler configuration.
- Job cancellation.
- Job retry.
- Schedule changes.
- Destructive maintenance actions.

Do not assume antiforgery protection applies automatically to every JSON endpoint.

Verify actual behavior with HTTP-level tests.

## 7. Secure Configuration and Secret Storage

Review all sensitive application configuration.

### Sensitive values

Include:

- Meilisearch master key.
- Meilisearch API keys.
- Administrator credentials.
- Authentication secrets.
- Data Protection keys.
- Future crawler credentials.

### Requirements

- Never expose secrets through Blazor components.
- Never return complete secret-bearing configuration objects to browsers.
- Never include secrets in logs.
- Never include secrets in job snapshots.
- Never persist plaintext passwords.
- Avoid hardcoded production secrets.
- Validate configuration before application startup.

### Windows protection

Use appropriate Windows or ASP.NET Core mechanisms.

Protect writable configuration and data directories through filesystem permissions.

Use ASP.NET Core Data Protection appropriately.

Persist Data Protection keys where necessary for reliable application restarts.

Ensure authentication behavior remains predictable after controlled restarts.

### Secret rotation

Document how sensitive keys can be replaced.

Do not implement a comprehensive automated secret rotation platform.

## 8. Meilisearch Network Isolation

Meilisearch must remain an internal service.

### Requirements

- Bind Meilisearch to loopback by default.
- Prevent direct intranet access.
- Require appropriate API authentication.
- Keep the master key server-side.
- Avoid exposing Meilisearch through the public application endpoint.
- Validate configured host and port.
- Restrict access to index management operations.
- Avoid leaking backend error details.

### Production behavior

The normal architecture must be:

```text
Browser
   |
   v
Lantern ASP.NET Core
   |
   v
Meilisearch
```

Not:

```text
Browser
   |
   v
Meilisearch
```

### Verification

Test that Meilisearch is accessible to Lantern while unavailable through unintended externally reachable interfaces.

Do not assume a loopback address in configuration proves the process actually bound correctly.

Verify effective listeners.

### Important limitation

A loopback service is reachable by other processes on the same machine.

Document that process-level isolation is not equivalent to authentication.

Do not rely solely on localhost binding for credential security.

## 9. Embedding Server Security

Audit the Phase 3.5 embedding server.

### Requirements

- Bind the embedding listener to loopback.
- Validate the effective listening address.
- Reject oversized requests.
- Enforce token and input limits.
- Preserve bounded queue capacity.
- Preserve bounded inference concurrency.
- Avoid logging full embedding inputs.
- Avoid logging vectors.
- Validate model file paths.
- Prevent arbitrary model file selection by untrusted users.
- Handle malformed requests safely.

### Inference worker

Verify:

- Single-worker inference remains safe.
- Concurrent HTTP requests do not cause concurrent unsafe access to LlamaPure.
- Queue cancellation releases resources.
- Request timeouts do not leak retained buffers.
- Worker exceptions are handled.
- Model initialization failures do not terminate unrelated search functionality.
- Graceful shutdown disposes native resources.

### Endpoint exposure

The embedding REST API is an internal integration endpoint.

Do not expose it through public routing or reverse-proxy configuration accidentally.

If the endpoint has no application-level authentication, document the local trust boundary.

### Failure behavior

Keyword search must remain available when the embedding server fails.

Hybrid search failures must be reported accurately.

Do not silently label keyword-only results as hybrid results.

## 10. Web Crawler SSRF Hardening

Audit the complete Phase 8 HTTP fetching path.

**SSRF protection is a critical Phase 10 requirement.**

### Validate all destinations

Validation must apply to:

- Start URLs.
- Discovered URLs.
- Redirect destinations.
- Canonical URL candidates used for fetching.
- Linked document URLs.
- Connection test targets.
- Retry destinations.

### Requirements

- Allow only HTTP and HTTPS.
- Reject embedded URL credentials.
- Validate hostnames.
- Validate destination ports.
- Resolve DNS.
- Evaluate IPv4 and IPv6 addresses.
- Enforce configured network approval policies.
- Validate redirects independently.
- Prevent DNS rebinding bypasses.
- Reject prohibited local service destinations.
- Reject cloud metadata endpoints.
- Avoid automatic unsafe redirects.

### Important connection rule

Do not validate a hostname using one DNS lookup and then allow HttpClient to connect to an unchecked address obtained from another lookup.

Ensure the connection uses a destination covered by the validated policy.

Use an appropriate .NET connection mechanism when necessary.

### Intranet support

Lantern must support explicitly approved internal websites.

Do not universally block private IP addresses.

Instead, enforce administrator-approved host, address-range, and port restrictions.

### Redirects

Keep automatic redirect handling disabled for the crawler HTTP client.

Validate every redirect destination before connecting.

Do not forward credentials to unapproved destinations.

### Proxy considerations

Review whether environment or system proxy settings can change the actual network destination.

Document supported proxy behavior.

Prevent proxy configuration from silently bypassing the intended network boundary policy.

### DNS changes

Test scenarios involving DNS records that change between validation and connection.

Do not rely solely on hostname string comparison.

## 11. Web Crawler Reliability

Audit web crawler limits and failure handling.

### Requirements

- Enforce maximum crawl depth.
- Enforce page-count limits.
- Enforce response size limits.
- Enforce redirect limits.
- Enforce bounded concurrency.
- Enforce per-host throttling.
- Respect robots.txt behavior.
- Apply request timeouts.
- Handle rate limiting.
- Prevent infinite crawl loops.
- Avoid unlimited retries.

### Deletion safety

Do not delete indexed documents because of:

- HTTP 401 or 403.
- HTTP 429.
- HTTP 5xx.
- DNS failures.
- TLS failures.
- Timeouts.
- Interrupted crawling.
- Incomplete discovery.
- robots.txt restrictions.
- Temporary intranet outages.

### HTTP 404/410

Confirmed missing-resource responses may support deletion reconciliation.

However, ensure the selected policy cannot cause accidental mass deletion.

Preserve retry and recovery behavior.

### URL state

Verify URL normalization and canonical identity.

Do not silently merge distinct URLs because of unsafe normalization.

Preserve stable document IDs where possible.

## 12. Filesystem Crawler Security

Audit Phase 5 and Phase 6 filesystem access.

### Approved roots

Crawler roots must be explicitly approved by administrators.

### Requirements

- Validate absolute paths.
- Validate UNC paths.
- Reject unsupported relative paths.
- Enforce configured filesystem boundaries.
- Prevent path traversal.
- Restrict symbolic link and junction traversal.
- Prevent traversal cycles.
- Handle Windows path normalization consistently.
- Handle inaccessible files safely.
- Handle network share disconnections.

### Filesystem links

Do not follow symbolic links or junctions by default.

If the implementation supports following them, validate the resolved destination.

Do not allow a link inside an approved root to escape into an unauthorized directory.

### Windows path edge cases

Review:

- Drive-letter paths.
- UNC shares.
- Case-insensitive paths.
- Alternate path representations.
- Reparse points.
- Long paths.
- File and directory races.

Do not rely only on string-prefix comparisons.

### File changes during processing

A file may be replaced or modified between validation and extraction.

Review time-of-check/time-of-use behavior.

Detect changes where practical.

Do not incorrectly mark an unstable file as successfully synchronized.

### Permissions

Use the Lantern service or application identity.

Do not assume browser-user filesystem permissions apply to the server process.

## 13. Document Extraction Safety

Document extraction processes untrusted files.

Audit the Phase 4 pipeline.

### Requirements

- Enforce maximum file sizes.
- Enforce extracted-text limits.
- Enforce extraction concurrency limits.
- Validate file formats.
- Handle malformed documents.
- Handle encrypted documents.
- Handle unsupported formats.
- Avoid uncontrolled temporary files.
- Avoid excessive memory allocations.
- Prevent ordinary extraction failures from crashing the application.

### Archive and compression risks

Office formats use ZIP-based containers.

Review decompression and expansion behavior.

Apply practical limits on decompressed content and extracted text.

Do not rely exclusively on compressed input file size.

### XML processing

Ensure XML-based extraction does not allow unsafe external entity resolution or unintended external resource loading.

Use safe parser configurations.

### HTML processing

Do not execute scripts.

Do not fetch external resources automatically during extraction.

Do not render extracted HTML directly into Blazor.

### Timeout limitations

A cancellation token does not guarantee that blocking native or library code stops immediately.

Document the actual timeout guarantees.

If an extractor can hang or consume unbounded resources in-process, consider a narrowly scoped isolation mechanism where justified.

Do not introduce a general distributed document-processing framework.

## 14. Search Input and Output Security

Audit the Phase 9 search interface and services.

### Query validation

- Bound query length.
- Bound page sizes.
- Bound pagination offsets.
- Validate sort fields.
- Validate category identifiers.
- Validate crawler identifiers.
- Validate document-type filters.
- Validate semantic ratios.
- Construct Meilisearch filters safely.

Do not accept arbitrary raw filter expressions from browser inputs.

### Result rendering

Treat indexed content as untrusted.

- Escape titles.
- Escape source paths.
- Escape snippets.
- Sanitize highlighting where necessary.
- Reject unsafe source URL schemes.
- Avoid rendering raw HTML.
- Avoid client-side script execution.

### Source navigation

Do not create unrestricted file-download endpoints.

Validate source URLs.

Do not let user-controlled paths cause the server to read arbitrary files.

### Categories

Category filtering must not be treated as authorization.

Do not assume category membership restricts document access.

Preserve the documented v1.0 common-access model.

## 15. Persistent Job Reliability

Audit the Phase 7 job execution system.

### Requirements

- SQLite remains the authoritative job store.
- Job submission persists before acknowledgment.
- Job claiming is concurrency-safe.
- Invalid state transitions are rejected.
- Per-crawler overlapping execution is prevented.
- Execution concurrency is bounded.
- Cancellation intent is persisted.
- Retry relationships are preserved.
- Job history survives restarts.
- Configuration snapshots remain consistent.

### Application crashes

On startup:

1. Identify interrupted running jobs.
2. Mark them according to the established recovery policy.
3. Preserve progress and diagnostics.
4. Release stale ownership.
5. Restore eligible pending jobs.
6. Recalculate schedules.
7. Avoid unsafe deletion reconciliation.

### Scheduled jobs

Verify:

- Duplicate scheduled occurrences are prevented.
- Missed occurrences follow the documented policy.
- Time zone handling is consistent.
- Disabled crawlers do not start.
- Schedule changes do not corrupt running jobs.

### Shutdown

Ensure hosted services receive cancellation.

Avoid abandoning background tasks without diagnostics.

Do not report controlled or interrupted shutdowns as successful crawl completion.

## 16. Meilisearch and SQLite Synchronization

SQLite and Meilisearch do not share a transaction.

**Do not attempt to simulate cross-system atomicity using unsafe assumptions.**

Audit all operations that update both systems.

### Relevant operations

- Document insertion.
- Document update.
- Chunk replacement.
- Document deletion.
- Chunk deletion.
- File crawl reconciliation.
- Web crawl reconciliation.
- Category metadata synchronization.

### Requirements

- Track Meilisearch asynchronous task completion.
- Distinguish accepted tasks from successful tasks.
- Preserve retry information.
- Avoid prematurely updating successful fingerprints.
- Avoid orphaned chunks where practical.
- Preserve the last valid searchable version during failed updates where possible.
- Make interrupted operations repairable.
- Avoid destructive recovery shortcuts.

### Idempotency

Repeated execution should not accumulate duplicate documents or chunks.

Deletion operations should be safely retryable.

Metadata updates should be idempotent.

### Failure recovery

Test interruptions between:

```text
Meilisearch update submitted
Meilisearch task completed
SQLite state committed
```

Ensure later execution can detect or repair incomplete synchronization.

Do not assume operation order alone provides atomicity.

## 17. Category Synchronization Reliability

Phase 9 introduced persistent search categories.

Audit category assignments and their synchronization into search indexes.

### Requirements

- SQLite remains authoritative for category assignments.
- Parent documents and chunks have consistent category metadata.
- Category changes remain retryable.
- Concurrent crawler indexing does not permanently restore stale assignments.
- Category deletion does not delete source documents.
- Uncategorized documents remain searchable through All.
- Category identifiers remain stable.
- Category updates do not alter document IDs.

### Embedding regeneration

Verify that category-only changes do not unnecessarily regenerate embeddings.

Do not assume every Meilisearch partial update preserves existing vectors.

Test the actual deployed Meilisearch behavior.

### Interrupted synchronization

If Lantern terminates during category updates:

- Preserve the intended SQLite assignment.
- Preserve synchronization status.
- Resume or repair remaining metadata updates.
- Avoid full recrawling.
- Avoid unnecessary re-extraction.

Do not add a second unrelated job framework.

Reuse existing persistent execution mechanisms where appropriate.

## 18. SQLite Reliability

Audit SQLite configuration and lifecycle.

### Requirements

- Use tracked EF Core migrations.
- Avoid destructive schema recreation.
- Handle database locking appropriately.
- Use correct DbContext lifetimes.
- Avoid concurrent use of one DbContext.
- Bound transaction duration.
- Handle failed migrations.
- Use safe database paths.
- Protect database files with appropriate permissions.

### Data integrity

Verify foreign keys and relevant constraints.

Check category relationships, crawler state, job records, and synchronization metadata.

Do not rely only on application-level validation.

### Corruption handling

Detect and report SQLite integrity failures.

Do not automatically delete or recreate a corrupted database.

Provide a documented recovery path.

## 19. Backup and Recovery

Implement and test a basic backup and restoration procedure.

### Data to protect

Include:

```text
Lantern Data
    |
    +-- SQLite Database
    |
    +-- Meilisearch Index Data
    |
    +-- Application Configuration
    |
    +-- Protected Secrets / Data Protection Keys
    |
    +-- Required Recovery Metadata
```

The embedding GGUF model may be handled as a separately provisioned resource if it can be restored reliably.

### Requirements

- Define a supported backup procedure.
- Define a supported restore procedure.
- Avoid copying a live SQLite database unsafely.
- Use SQLite's supported backup mechanisms or coordinate database shutdown.
- Use supported Meilisearch snapshot/dump or coordinated data backup mechanisms.
- Preserve required configuration.
- Preserve crawler definitions.
- Preserve category assignments.
- Preserve execution history where expected.
- Protect backup files against unauthorized access.

### Consistency

A SQLite backup and a Meilisearch snapshot may represent different moments in time.

Do not claim atomic consistency across both systems without implementing it.

A simple coordinated maintenance window or controlled quiescence is acceptable.

Document how to reconcile differences after restoration.

### Restore validation

Test a complete restore into an isolated data directory or environment.

Verify:

1. SQLite opens successfully.
2. Migrations remain valid.
3. Crawler configurations exist.
4. Category relationships are preserved.
5. Jobs and schedules are readable.
6. Meilisearch indexes are usable.
7. Keyword search works.
8. Hybrid search works when the embedding model is available.
9. Existing document and chunk identities remain consistent.

### Security

Backup files may contain indexed internal documents and secrets.

Do not expose backups through public HTTP endpoints.

Do not leave unencrypted sensitive backups in publicly accessible directories.

## 20. Service Interruption and Recovery

Test ordinary infrastructure failures.

### Meilisearch unavailable

Expected behavior:

- Search reports an understandable error.
- Administration remains accessible where possible.
- Job failures are recorded.
- Successful synchronization state is not updated incorrectly.
- Retry remains possible.

### Embedding server unavailable

Expected behavior:

- Keyword search continues.
- Hybrid search reports unavailable status.
- Embedding-dependent indexing failures are recorded.
- Pending synchronization remains recoverable.

### SQLite unavailable

Expected behavior:

- Persistent operations fail clearly.
- Jobs are not falsely acknowledged as durably submitted.
- The application does not silently fall back to volatile state.

### Filesystem root unavailable

Expected behavior:

- Crawl failure is recorded.
- No mass deletion occurs.
- Previous indexed content remains available.

### Web target unavailable

Expected behavior:

- Errors are recorded.
- Temporary failures do not trigger mass deletion.
- Subsequent retries can recover.

### Application termination

Expected behavior:

- Running jobs are identified as interrupted.
- Pending jobs remain recoverable.
- Incomplete scans do not reconcile deletions.

## 21. Least-Privilege Operation

Document and enforce reasonable process permissions.

### Application identity

The Lantern process should use only the permissions required for:

- Application data.
- SQLite database.
- Meilisearch data.
- Model files.
- Approved crawler source directories.
- Required network destinations.

### File crawler access

Prefer read-only permissions to source directories.

Do not require write access merely to extract and index documents.

### Runtime data

Separate writable application data from installation files.

Do not rely on administrator-level privileges for ordinary operation.

### Windows service preparation

Document suitable service-account permissions.

Do not implement Windows service packaging or installation in Phase 10.

That belongs to Phase 12.

## 22. Logging and Diagnostics Security

Audit application logging.

### Requirements

- Use structured logging.
- Preserve useful correlation identifiers.
- Report failures without exposing secrets.
- Avoid logging full extracted documents.
- Avoid logging embedding vectors.
- Avoid logging passwords.
- Avoid logging API keys.
- Avoid exposing protected configuration snapshots.

### Sensitive paths and URLs

Filesystem paths and intranet URLs may contain sensitive information.

Limit their exposure to authorized diagnostics.

### Error responses

Do not return raw stack traces to ordinary users.

Keep detailed diagnostics in appropriately protected server logs.

### Retention

Respect existing job and event retention settings.

Avoid unbounded log growth.

Do not implement a large observability platform.

## 23. Security Configuration Validation

Validate security-critical configuration at startup.

Examples include:

```text
Meilisearch Bind Address
Embedding Server Bind Address
Authentication Settings
Data Protection Key Storage
Approved File Roots
Approved Web Networks
Crawler Resource Limits
Database Path
```

### Requirements

- Reject dangerous production configurations.
- Report actionable errors.
- Avoid silently falling back to insecure defaults.
- Preserve safe keyword-only operation when optional embedding services are disabled.

### Development versus production

Development configurations may differ from production.

However, do not allow development-only insecure behavior to become the default production configuration.

Document the distinction.

## 24. Security Test Infrastructure

Add deterministic security tests.

Prefer isolated temporary resources.

Do not attack arbitrary external systems.

### Required test categories

- Authentication.
- Authorization.
- CSRF protection.
- Input validation.
- Filesystem boundaries.
- Web destination validation.
- Redirect handling.
- DNS changes.
- HTML rendering safety.
- Extraction resource limits.
- Internal service isolation.
- Secret exposure prevention.

### Test principles

Use controlled local HTTP fixtures, test databases, and temporary directories.

Avoid tests that depend on arbitrary production services.

Do not disable security protections merely to make tests pass.

Do not run destructive tests against production indexes.

## 25. Authentication and Authorization Tests

Test:

1. Anonymous administration access.
2. Valid administrator login.
3. Invalid credentials.
4. Logout.
5. Session expiration.
6. Unauthorized settings modification.
7. Unauthorized category modification.
8. Unauthorized crawler modification.
9. Unauthorized path testing.
10. Unauthorized web connection testing.
11. Unauthorized job submission.
12. Unauthorized job cancellation.
13. Unauthorized schedule changes.
14. Cross-site state-changing requests.
15. Open redirect prevention.
16. Login throttling.

Use HTTP-level integration tests where practical.

Verify actual server behavior rather than only testing authorization attributes.

## 26. Web Crawler Security Tests

Test:

1. Unsupported URL schemes.
2. URL-embedded credentials.
3. Unapproved hosts.
4. Unapproved ports.
5. Approved intranet destinations.
6. Unapproved private addresses.
7. Loopback targets.
8. Link-local targets.
9. Cloud metadata endpoints.
10. IPv4 and IPv6 handling.
11. DNS address changes.
12. Redirects to forbidden destinations.
13. Redirect loops.
14. Redirect count limits.
15. HTTP response size limits.
16. Excessive link discovery.
17. robots.txt restrictions.
18. Crawler cancellation.
19. Partial discovery.
20. Connection-test restrictions.

Ensure security validation is exercised through the actual HTTP fetching path.

Do not claim SSRF protection is complete based only on URL string validation.

## 27. Filesystem Security Tests

Test:

1. Approved root directories.
2. Disallowed root directories.
3. Relative paths.
4. Path traversal.
5. Windows path normalization.
6. UNC paths.
7. Symbolic links.
8. Junctions and reparse points where supported.
9. Files replaced during extraction.
10. Directory enumeration failures.
11. Permission failures.
12. Disconnected network shares.
13. Canceled scans.
14. Incomplete scans.
15. Prevention of unsafe deletion reconciliation.

Use controlled temporary filesystem fixtures.

For Windows-specific cases, run tests on Windows where practical.

Clearly document tests not executed due to platform limitations.

## 28. Extraction Security Tests

Test:

1. Malformed PDF.
2. Malformed DOCX.
3. Malformed XLSX.
4. Malformed PPTX.
5. Encrypted documents.
6. Oversized files.
7. Excessive extracted text.
8. ZIP expansion limits.
9. Invalid XML.
10. Unsafe XML external references.
11. Malformed HTML.
12. Invalid text encodings.
13. Binary files disguised as text.
14. Cancellation.
15. Extraction timeout behavior.

Preserve Unicode and source-code formatting behavior.

Do not introduce unsafe text normalization.

## 29. Persistence and Recovery Tests

Use real SQLite and Meilisearch instances where required.

### SQLite

Test:

- Database migrations.
- Concurrent writes.
- Foreign key constraints.
- Recovery after interrupted writes.
- Backup and restore.
- Integrity checks.
- Persistence after restart.

### Meilisearch

Test:

- Indexing task failures.
- Incomplete indexing tasks.
- Document updates.
- Chunk replacement.
- Chunk deletion.
- Category metadata synchronization.
- Repeated operations.
- Service interruption.

### Cross-system recovery

Test interruption points such as:

```text
SQLite intent recorded
    |
    v
Meilisearch task submitted
    |
    v
Meilisearch task completed
    |
    v
SQLite success state committed
```

Simulate interruptions between these stages.

Verify that subsequent execution can recover or reconcile state without data loss or duplicate accumulation.

### Backup restoration

Test an isolated restoration using both SQLite and Meilisearch data.

Do not call a backup strategy verified unless restoration was actually tested.

## 30. Job Recovery Tests

Test:

1. Pending job restoration.
2. Interrupted running jobs.
3. Cancellation requests during shutdown.
4. Duplicate claim prevention.
5. Per-crawler execution exclusion.
6. Retry after partial indexing.
7. Interrupted deletion reconciliation.
8. Missed recurring schedules.
9. Duplicate scheduled occurrence prevention.
10. Category changes during active crawling.
11. Meilisearch downtime during execution.
12. SQLite write failures.

Preserve historical execution records.

Do not convert interrupted jobs into successful jobs.

## 31. Reliability Under Resource Pressure

Test bounded behavior under realistic load.

### Scenarios

- Many discovered files.
- Many discovered URLs.
- Large PDF documents.
- Large Office documents.
- Large extracted text.
- Slow embedding inference.
- Embedding queue saturation.
- Meilisearch indexing backlog.
- Concurrent crawler jobs.
- Repeated administrator requests.

### Requirements

- No unbounded task creation.
- No unbounded request queues.
- No unlimited document buffering.
- No unlimited retry loops.
- No uncontrolled temporary-file growth.
- No uncontrolled database write amplification.

### Measurements

Where practical, record:

- Peak memory usage.
- Queue saturation behavior.
- Request rejection behavior.
- Recovery duration.
- Indexing backlog.
- Job completion behavior.

Do not invent performance measurements.

Do not implement a distributed resource scheduling platform.

## 32. Security Audit Report

Produce a concise security audit report.

Suggested document:

```text
docs/security-audit.md
```

Include:

1. Reviewed components.
2. Identified vulnerabilities.
3. Implemented mitigations.
4. Remaining risks.
5. Security assumptions.
6. Tests performed.
7. Tests not performed.
8. Deployment requirements.
9. Known v1.0 limitations.

### Severity

Classify findings as:

```text
Critical
High
Medium
Low
Informational
```

Prioritize exploitable security issues over cosmetic improvements.

Do not claim the project is free of vulnerabilities.

Document unresolved findings transparently.

## 33. Reliability and Recovery Documentation

Create or update:

```text
docs/security.md
docs/backup-recovery.md
docs/operations.md
```

Adapt filenames to existing conventions.

### Security documentation

Explain:

- Authentication.
- Authorization.
- Network isolation.
- Crawler boundaries.
- Filesystem permissions.
- Secret handling.
- Embedding server exposure.
- Search access model.

### Recovery documentation

Explain:

- SQLite backup.
- Meilisearch backup.
- Consistent backup strategy.
- Restoration.
- Interrupted jobs.
- Failed indexing.
- Category synchronization recovery.
- Search index recovery.

### Operations documentation

Explain:

- Service startup dependencies.
- Safe restart behavior.
- Health checks.
- Resource limits.
- Common failure modes.
- Troubleshooting.

Avoid duplicating existing documentation unnecessarily.

## 34. Architecture Guidelines

Preserve the existing architecture.

Suggested areas of responsibility:

```text
Lantern.Web
    |
    +-- Authentication
    +-- Authorization
    +-- Antiforgery
    +-- Search validation
    +-- Safe result rendering

Lantern.Infrastructure
    |
    +-- SQLite reliability
    +-- Meilisearch task recovery
    +-- Category synchronization
    +-- Backup support
    +-- Secret handling

Lantern.Crawling
    |
    +-- Filesystem boundaries
    +-- Web SSRF protection
    +-- Resource limits
    +-- Job recovery
    +-- Safe reconciliation

Lantern.Embedding
    |
    +-- Loopback listener
    +-- Bounded queue
    +-- Model lifecycle
    +-- Inference failure handling
```

This structure is illustrative.

Do not create unnecessary projects or abstractions.

Prefer focused changes to existing components.

Avoid adding external infrastructure.

## 35. Out of Scope

Do not implement:

- Windows release packaging.
- Windows service installation.
- CI pipeline creation.
- Automated release publishing.
- Multi-tenant authentication.
- Active Directory integration.
- Per-document Windows ACL synchronization.
- Enterprise authorization policies.
- Distributed crawler execution.
- Multi-node job coordination.
- External message queues.
- Advanced monitoring dashboards.
- New crawler types.
- OCR.
- RAG.
- Chat interfaces.
- Model training.
- GPU inference.
- Automatic vulnerability remediation services.
- Cloud-dependent security services.

These features belong to later phases or deferred enhancements.

## 36. Acceptance Criteria

Phase 10 is complete only when all required conditions are satisfied.

### Authentication and authorization

- Administrator authentication is secure.
- Administrative routes require authorization.
- Administrative operations enforce server-side authorization.
- State-changing requests have appropriate CSRF protection.
- Unauthorized users cannot execute privileged operations.
- Session handling is secure.

### Network isolation

- Meilisearch is not directly exposed to intranet clients.
- The embedding server is restricted to intended local access.
- Meilisearch credentials remain server-side.
- Effective listener bindings are verified.
- Internal service failures do not expose secrets.

### Web crawler security

- Crawl boundaries are enforced.
- Redirects cannot bypass destination restrictions.
- DNS changes cannot bypass connection validation.
- Unsafe network targets are rejected.
- Approved intranet targets work.
- Resource limits are enforced.
- Incomplete crawls cannot cause mass deletion.

### Filesystem security

- Unapproved filesystem roots are rejected.
- Path traversal is prevented.
- Symbolic links and junctions cannot bypass configured boundaries.
- UNC failures are handled safely.
- Incomplete scans do not trigger unsafe deletion.
- Ordinary crawling does not require unnecessary filesystem write privileges.

### Search and embedding

- Search inputs are validated.
- Raw untrusted HTML is not rendered.
- Unsafe source navigation is rejected.
- Embedding requests are bounded.
- Native inference failures are handled.
- Keyword search remains available without embedding inference.

### Data reliability

- SQLite state survives ordinary application restarts.
- Job interruption recovery works.
- Meilisearch failures are handled.
- Category synchronization remains recoverable.
- Parent documents and chunks can be reconciled.
- Repeated operations remain idempotent.
- Partial failures do not silently produce successful synchronization state.

### Backup and restore

- A supported backup procedure exists.
- SQLite backup and restoration are verified.
- Meilisearch backup and restoration are verified.
- Configuration and protected secrets are accounted for.
- An isolated restore test has been executed.
- Consistency limitations are documented.

### Testing and documentation

- The solution builds.
- Existing Phase 0–9 tests pass.
- New security tests pass.
- Recovery tests pass.
- Fault-injection tests pass where applicable.
- Security boundaries are documented.
- Known limitations are documented.
- Outstanding high-severity findings are explicitly reported.

Do not declare Phase 10 fully complete while a known critical or high-severity security issue remains unresolved.

## 37. Deliverables

Provide:

1. Security audit of existing components.
2. Authentication and authorization fixes.
3. Antiforgery and input validation hardening.
4. Secure Meilisearch configuration.
5. Secure embedding server configuration.
6. Web crawler SSRF hardening.
7. Filesystem boundary hardening.
8. Extraction resource safety improvements.
9. Search input and output protection.
10. Persistent job recovery improvements.
11. SQLite and Meilisearch synchronization recovery.
12. Category synchronization reliability improvements.
13. Backup and restore procedure.
14. Fault-injection tests.
15. Security regression tests.
16. Updated operational documentation.
17. Security audit report.

Do not create unnecessary infrastructure or unrelated application features.

## 38. Required Completion Report

At the end of implementation, provide the following.

### A. Implementation summary

Describe security and reliability changes.

### B. Files changed

List important created and modified files.

### C. Security findings

For each material finding, report:

- Severity.
- Affected component.
- Failure or attack scenario.
- Implemented mitigation.
- Verification method.
- Remaining risk.

### D. Architectural decisions

Explain:

- Authentication and authorization.
- Network isolation.
- SSRF protection.
- Filesystem boundaries.
- Secret storage.
- Data synchronization.
- Job recovery.
- Backup and restoration.

### E. Build and test commands

List the actual commands executed.

### F. Test results

Clearly distinguish:

- Passed tests.
- Failed tests.
- Skipped tests.
- Tests not executed.

### G. Backup and restore verification

Document the procedure used and the observed result.

Do not claim recovery is verified unless restoration was actually executed.

### H. Performance observations

Report measured performance impact where available.

Do not invent benchmarks.

### I. Known limitations

Document remaining security and reliability limitations.

### J. Remaining work

Identify unresolved findings and incomplete acceptance criteria.

Do not begin Phase 11 automatically.

---

## Final Instructions

Implement Phase 10 directly in the existing Lantern repository.

Inspect and preserve all functionality from Phases 0–9.

Prioritize security auditing, targeted fixes, fault isolation, data consistency, and recovery testing.

Do not add major new product features.

Use standard ASP.NET Core security mechanisms.

Keep Meilisearch and the embedding server isolated from intranet clients.

Enforce administrator authorization for all privileged operations.

Harden web crawling against SSRF, unsafe redirects, DNS rebinding, and unapproved internal network access.

Harden filesystem crawling against path traversal, symlink escapes, unsafe root configuration, and incomplete scans.

Preserve bounded document extraction and embedding inference.

Ensure crawler jobs, categories, documents, chunks, and indexes remain recoverable after ordinary failures.

Verify real backup and restoration procedures.

Do not hide failed tests or unresolved vulnerabilities.

Maintain the original Lantern philosophy: simple, self-hosted, Windows-first, maintainable, and independent of unnecessary external infrastructure.

**The primary success condition is a hardened Lantern application that can operate safely in a Windows intranet environment, withstand ordinary service interruptions and malformed inputs, recover from partial indexing and crawler failures, and restore its configuration and searchable data without compromising established security boundaries.**

Do not implement Phase 11 or later features automatically.
