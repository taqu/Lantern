# Lantern — Investigate and Fix System Status Inconsistency with Meilisearch

## Objective

Investigate and fix an inconsistency between Lantern's System Status page and the actual behavior of its Meilisearch backend.

The System Status page currently reports:

```text
System Status

Search Engine

Status:
  The search engine rejected the request.
  Check server credentials and permissions.

Active index:
  lantern-dev-documents

Indexed documents:
  Unavailable
```

However, the user reports that database updates are possible.

This suggests that the status-check implementation may be using a different credential, endpoint, client configuration, or permission set than the functioning data-update path.

It may also indicate that successful SQLite operations are being confused with successful Meilisearch operations.

**Your task is to determine why the displayed status differs from actual subsystem behavior, correct the underlying implementation, and ensure that System Status reports accurate, verifiable information.**

Do not merely change the UI message or suppress the error.

Do not grant broader Meilisearch permissions without identifying the exact operation that requires them.

Do not regenerate credentials or delete indexes as a troubleshooting shortcut.

---

## Phase 1 — Inspect the System Status Implementation

### 1. Locate the Status Page and Backend

Inspect:

- System Status Blazor components.
- System Status endpoints.
- Health-check services.
- Meilisearch connectivity checks.
- Index metadata retrieval.
- Index statistics retrieval.
- Document count retrieval.
- Status DTOs and view models.
- UI error mapping.
- Status refresh and caching.
- Meilisearch SDK client factories.
- Role-based credential management.
- Application startup and index initialization.

Identify the exact code that produces:

```text
The search engine rejected the request.
Check server credentials and permissions.
```

Determine whether this message is generated from:

- HTTP 401.
- HTTP 403.
- `missing_authorization_header`.
- `invalid_api_key`.
- Missing Meilisearch permissions.
- Missing index.
- An SDK exception.
- A generic catch block.
- An unrelated failure incorrectly classified as authentication.

Do not assume the displayed message accurately identifies the underlying failure.

### 2. Trace Each Displayed Field

For every System Status field, identify its data source.

| Field | Required investigation |
|---|---|
| Search Engine Status | What request determines health? |
| Active Index | Configured UID or verified existing index? |
| Indexed Documents | Which API supplies the count? |
| Refresh Time | When was the state actually queried? |
| Error Message | Which exception and response produced it? |

Determine whether some values come from configuration while others come from live Meilisearch responses.

Do not present configured values as verified operational state.

### 3. Reproduce the Failure

Run Lantern in the affected development configuration.

Capture:

- Meilisearch base URL.
- Active index UID.
- HTTP method and API route.
- Credential role used.
- HTTP response status.
- Meilisearch error code.
- Required permission.
- SDK call stack.
- Status service result.
- UI-rendered result.

Never log actual API key values or Authorization headers.

Use structured diagnostics and redact secrets.

---

## Phase 2 — Compare Working and Failing Operations

### 4. Verify What "Database Updates Work" Means

Determine exactly which update operation succeeds.

Possible cases include:

1. SQLite metadata updates.
2. Crawler configuration updates.
3. Meilisearch document submission.
4. Completed Meilisearch document indexing.
5. Searchable document updates.
6. Index settings updates.

These are not equivalent.

Verify the actual operation rather than inferring backend health from a UI success notification.

For Meilisearch document updates, inspect the resulting asynchronous task and confirm successful completion.

### 5. Compare Credential Usage

Trace the credentials used by:

- Normal search.
- Document indexing.
- Document updates.
- Document deletion.
- Index initialization.
- System Status.
- Index statistics.
- Administrative diagnostics.

Record the intended role and actual role for each operation.

Expected roles include:

```text
Search API Key
Indexing API Key
Admin API Key
Internal Maintenance API Key
```

The Master Key must not be used for routine status requests.

### Critical investigation

Determine whether System Status is:

- Using an anonymous Meilisearch client.
- Using a Search API Key without statistics permissions.
- Using an expired key.
- Using a key scoped to the wrong index.
- Using credentials from an outdated configuration file.
- Connecting to a different Meilisearch instance.
- Holding a stale SDK client after credential rotation.
- Using a shared client with incorrect dependency injection lifetime.

Do not assume all Meilisearch clients use the same configuration.

---

## Phase 3 — Verify Meilisearch API Permissions

### 6. Identify the Exact Status Endpoints

Inspect the actual requests issued by the status service.

Relevant Meilisearch APIs may include:

```http
GET /health
GET /indexes/{index_uid}
GET /indexes/{index_uid}/stats
GET /stats
GET /tasks
```

Do not assume that these endpoints require identical permissions.

Verify the requirements against the pinned Meilisearch version.

### 7. Validate the Permission Model

Relevant Meilisearch actions include:

| Action | Typical purpose |
|---|---|
| `search` | Execute searches |
| `documents.add` | Insert or update documents |
| `documents.get` | Retrieve documents |
| `indexes.get` | Retrieve index metadata |
| `stats.get` | Retrieve index statistics |
| `tasks.get` | Inspect asynchronous tasks |

The actual status implementation must request only the permissions it needs.

A key that can submit document updates is not automatically authorized to retrieve statistics.

A Search API Key may also lack index metadata or statistics permissions.

### 8. Correct Status Credentials

Implement one consistent approach.

**Preferred approach: dedicated read-only diagnostics capability**

Create a narrowly scoped server-side credential or role for System Status if that best matches the existing credential architecture.

Its permissions should include only the required diagnostic actions.

For example:

```json
{
  "name": "Lantern Status",
  "actions": [
    "indexes.get",
    "stats.get"
  ],
  "indexes": [
    "lantern-dev-documents"
  ],
  "expiresAt": null
}
```

This is illustrative.

Adapt the action list, index scopes, and expiration policy to the actual application requirements.

Include the chunk index if its statistics are displayed.

If the existing Internal Maintenance credential already has the required read permissions, consider reusing the existing authorized service instead of creating an unnecessary new persistent credential.

### Requirements

- Keep diagnostic credentials server-side.
- Never expose them to Blazor browser state.
- Never place them in authentication cookies.
- Do not use the Master Key for routine monitoring.
- Do not grant `*` permissions as a workaround.
- Do not give the Search API Key administrative write permissions.

### 9. Integrate with API Key Lifecycle Management

If the status service requires a managed credential, integrate it with the existing Meilisearch credential manager.

Ensure:

- Initial provisioning.
- Existing key reuse.
- Permission validation.
- Expiration validation.
- Rotation.
- Client refresh after rotation.
- Safe recovery after interrupted provisioning.

Do not create a special hardcoded API key for the status page.

Do not regenerate keys whenever the status page is opened.

---

## Phase 4 — Correct Health and Readiness Semantics

### 10. Separate Connectivity from Authorization

System Status must not treat all failed requests as the same condition.

Distinguish:

- Meilisearch process not running.
- Network connection failure.
- Meilisearch health endpoint failure.
- Credential unavailable.
- Authentication rejected.
- Insufficient permissions.
- Index not found.
- Statistics unavailable.
- Indexing tasks pending or failed.

For example:

```text
Meilisearch process:
  Running

Connectivity:
  Available

Search authorization:
  Valid

Indexing authorization:
  Valid

Statistics:
  Permission denied
```

These states can coexist.

### 11. Do Not Treat Statistics Failure as Engine Failure

A successful health check does not guarantee that every authenticated operation works.

Conversely, a denied statistics request does not prove the search engine is unavailable.

Do not report the entire search engine as failed merely because a diagnostic endpoint lacks authorization.

Implement separate capability checks.

### 12. Distinguish Health from Readiness

Suggested semantics:

**Health**

Whether the Meilisearch service is responding.

**Search readiness**

Whether the configured search index can be queried with the Search API Key.

**Indexing readiness**

Whether the indexing subsystem has valid credentials and the required index configuration.

**Statistics availability**

Whether the status service can retrieve index statistics.

**Hybrid readiness**

Whether the embedding and chunk-search infrastructure is actually available, if implemented.

Do not label a subsystem ready based solely on successful process startup.

Avoid writing test documents as part of routine status refresh.

Use non-destructive capability checks appropriate to each subsystem.

---

## Phase 5 — Fix Active Index and Document Count Reporting

### 13. Verify the Active Index

The current UI displays:

```text
lantern-dev-documents
```

Determine whether this is:

- A configured index UID.
- A verified existing index.
- A cached value.
- A fallback constant.

If it comes only from configuration, label it accordingly.

For example:

```text
Configured index:
  lantern-dev-documents

Index existence:
  Verified
```

Do not report an index as available without checking its actual state.

### 14. Correct Indexed Document Count Retrieval

Inspect the source of the document count.

The intended Meilisearch index statistics endpoint is:

```http
GET /indexes/{index_uid}/stats
```

Verify whether the existing SDK call actually uses this endpoint.

Confirm that the credential has the required permission.

Read the document count from the correct response property.

Do not derive the count from:

- SQLite crawler records.
- Number of discovered files.
- Submitted indexing requests.
- Pending task counts.
- Search result estimates.

unless the field is explicitly labeled as such.

### 15. Distinguish Stored and Indexed Documents

Lantern maintains different representations of documents.

Potential counts include:

- SQLite document metadata records.
- Discovered crawler files.
- Successfully extracted documents.
- Parent documents indexed in Meilisearch.
- Chunk documents indexed in Meilisearch.

These must not be confused.

If useful, the System Status page may report:

```text
SQLite document records:
  <count>

Meilisearch parent documents:
  <count>

Meilisearch chunks:
  <count>
```

Use actual measured counts and meaningful labels.

Do not introduce a new count unless it can be retrieved reliably.

### 16. Handle Pending Indexing Tasks

Meilisearch indexing operations are asynchronous.

A document update request may succeed while its indexing task remains pending or later fails.

Ensure System Status does not interpret a submitted task as an indexed document.

If appropriate, expose:

- Pending indexing tasks.
- Failed indexing tasks.
- Last completed synchronization.
- Last indexing error.

Keep these separate from the actual indexed document count.

---

## Phase 6 — Fix Status Service Error Handling

### 17. Introduce Structured Status Results

Avoid relying on one generic success/failure boolean.

Use an appropriate structured result model.

Conceptually:

```csharp
public sealed record SearchEngineStatus
{
    public required string ConfiguredIndexUid { get; init; }

    public required bool IsReachable { get; init; }

    public required bool IsSearchReady { get; init; }

    public required bool IsIndexingReady { get; init; }

    public int? IndexedDocumentCount { get; init; }

    public string? StatisticsErrorCode { get; init; }

    public DateTimeOffset CheckedAtUtc { get; init; }
}
```

This model is illustrative.

Adapt it to the existing status architecture.

Use separate capability results where a single boolean would lose important information.

### 18. Preserve Specific Errors

Do not convert every exception into:

```text
The search engine rejected the request.
Check server credentials and permissions.
```

Map actual conditions to distinct internal error categories.

For example:

```text
ConnectionUnavailable
MissingCredential
AuthenticationFailed
PermissionDenied
IndexNotFound
IndexStatisticsUnavailable
RequestTimedOut
UnexpectedServerError
```

Preserve the real Meilisearch error code in protected diagnostics.

Avoid exposing credentials or raw internal exception details to ordinary users.

### 19. Correct Dependency Injection Lifetimes

Inspect how the status service obtains Meilisearch clients.

Verify that:

- Client configuration matches the active environment.
- Credential loading completes before client creation.
- Credential rotation invalidates stale clients.
- Clients are not cached indefinitely with expired keys.
- Status checks do not reuse unauthorized clients.
- Background and UI services cannot mutate shared credentials accidentally.

Do not use global environment variables as the sole credential source.

---

## Phase 7 — Fix Blazor System Status UI

### 20. Display Independent Subsystem States

The System Status page should distinguish operational capabilities.

Suggested presentation:

```text
System Status

Search Engine

Connectivity:
  Healthy

Configured index:
  lantern-dev-documents

Index:
  Available

Search:
  Ready

Indexing:
  Ready

Indexed documents:
  125

Last checked:
  2026-11-19 06:00 UTC
```

These values are illustrative, not expected test results.

If statistics fail but search works, display:

```text
Connectivity:
  Healthy

Search:
  Ready

Indexed documents:
  Unavailable

Statistics:
  Permission denied
```

Do not report the entire engine as unavailable.

### 21. Avoid Stale Status

Investigate whether the Blazor page caches the status indefinitely.

Verify:

- Initial load.
- Manual refresh.
- Periodic refresh, if supported.
- Credential rotation.
- Meilisearch restart.
- Index creation.
- Index deletion.
- Navigation away and back.
- Blazor circuit reconnection.

Ensure a previously failed status does not remain visible after the underlying problem has been corrected.

### 22. Preserve Last Successful Data Safely

If the current request fails, do not present previously fetched statistics as current without qualification.

If a cached value is displayed, label it with its timestamp.

For example:

```text
Indexed documents:
  125

Last verified:
  10 minutes ago

Current statistics:
  Unavailable
```

Do not silently replace failures with cached success values.

---

## Phase 8 — Diagnose Credential Provisioning and Migration

### 23. Audit Existing Managed API Keys

Inspect the credentials created during setup.

Identify:

- Search API Key permissions.
- Indexing API Key permissions.
- Admin API Key permissions.
- Internal Maintenance permissions.
- Any dedicated diagnostic key.
- Index scopes.
- Expiration dates.
- Credential storage paths.

Determine whether the refactoring omitted `stats.get` or `indexes.get` from the intended diagnostic role.

### 24. Correct Existing Installations Safely

If existing managed API keys lack required permissions, implement a compatible reconciliation or rotation.

Do not assume an existing API key can have all security-sensitive permissions changed in place.

Use the supported Meilisearch Keys API and the established key lifecycle manager.

Requirements:

- Identify the affected managed credential.
- Create a correctly scoped replacement if necessary.
- Persist it securely.
- Verify the replacement works.
- Switch the status client.
- Retire the old managed key only when safe.
- Preserve unrelated keys and users.
- Avoid duplicate key creation.

Do not modify the Search API Key to grant broad administrator permissions.

### 25. Preserve Existing State

Do not:

- Reset the Meilisearch Master Key.
- Delete the existing index.
- Clear indexed documents.
- Delete SQLite databases.
- Regenerate Lantern administrator credentials.
- Remove crawler configuration.
- Force a full reindex.

This issue should be fixable without destructive data operations.

---

## Phase 9 — Automated Tests

### 26. Role-Based Permission Tests

Use real authenticated Meilisearch.

Test:

1. Search API Key can perform search.
2. Search API Key cannot perform unauthorized administrative operations.
3. Indexing API Key can perform required document operations.
4. Indexing API Key does not automatically have statistics access.
5. Diagnostics credential can retrieve index metadata.
6. Diagnostics credential can retrieve index statistics.
7. Diagnostics credential cannot modify documents or settings unless explicitly required.
8. Expired diagnostic credential is handled correctly.
9. Invalid diagnostic credential is handled correctly.
10. Credential rotation updates the status service.

Verify both permitted and forbidden operations.

### 27. Status Service Tests

Test:

- Meilisearch unavailable.
- Health endpoint reachable.
- Index exists.
- Index missing.
- Statistics authorized.
- Statistics denied.
- Search authorized.
- Search denied.
- Indexing authorized.
- Indexing denied.
- Missing credentials.
- Wrong index scope.
- Expired credentials.
- Stale SDK client.
- Request timeout.
- Unexpected server error.

Verify that one failed capability does not incorrectly mark every other capability as failed.

### 28. Real Index Statistics Test

Use a controlled Meilisearch test instance.

Test sequence:

1. Create a test index.
2. Insert a known number of test documents.
3. Wait for successful indexing task completion.
4. Retrieve index statistics.
5. Verify the reported document count.
6. Delete one document.
7. Wait for task completion.
8. Retrieve statistics again.
9. Verify the updated count.

Do not assume that a successful insertion response means the document count has already changed.

### 29. Integration Test for the Reported Inconsistency

Reproduce the key scenario:

1. Start authenticated Meilisearch.
2. Configure separate Search and Indexing credentials.
3. Configure a status client without `stats.get`.
4. Verify that permitted document operations can succeed.
5. Verify that statistics retrieval is denied.
6. Verify the status UI identifies this as a statistics permission problem.
7. Provision or select the correct diagnostics credential.
8. Refresh status.
9. Verify that actual document statistics are displayed.
10. Confirm that normal search and indexing still work.

This is a mandatory regression scenario.

### 30. SQLite Versus Meilisearch Test

Create or update a SQLite metadata record without indexing a corresponding document.

Verify that System Status does not report the SQLite record as an indexed Meilisearch document.

Then successfully index the document and verify the Meilisearch count separately.

This prevents false health conclusions based on the wrong data store.

### 31. UI Tests

Verify:

- Accurate status text.
- Correct error categorization.
- Independent capability reporting.
- Manual refresh.
- Updated status after credential correction.
- No stale success state.
- No stale failure state.
- Correct indexed document count.
- No credential leakage.

Use the repository's existing Blazor UI testing strategy.

---

## Phase 10 — Windows and Linux Verification

### 32. Windows

Verify:

- Development setup.
- Visual Studio F5.
- Correct Meilisearch instance.
- Correct secret loading.
- Authenticated status requests.
- Index statistics.
- Document updates.
- Search.
- Credential rotation.
- Windows Service operation where available.

### 33. Linux

Verify the same functionality in the available Linux environment.

Include:

- Secret file permissions.
- Service-user identity.
- systemd environment.
- Credential loading.
- Statistics permissions.
- Search and indexing.
- Application restart.
- Status refresh.

Do not assume Linux is correct merely because the secret files are readable.

Validate the actual API requests.

---

## Phase 11 — Documentation

### 34. Update Diagnostic Documentation

Explain the difference between:

- Engine connectivity.
- Search readiness.
- Indexing readiness.
- Statistics availability.
- SQLite metadata.
- Meilisearch parent documents.
- Meilisearch chunk documents.

Document required credentials and permissions for status retrieval.

### 35. Update English and Japanese Documentation

Update relevant pages in:

```text
docs/en/
docs/ja/
```

Also update:

- README if necessary.
- Setup documentation.
- Credential architecture documentation.
- Developer debugging documentation.
- System Status documentation.

Explain how to troubleshoot:

```text
missing_authorization_header
invalid_api_key
permission denied
index_not_found
statistics unavailable
```

Only document commands and configuration that actually exist.

---

## Acceptance Criteria

The task is complete when:

- The exact failing System Status API request is identified.
- The actual HTTP status and Meilisearch error code are known.
- Working database operations are accurately classified as SQLite or Meilisearch operations.
- Search and indexing credential roles are verified.
- Index statistics are requested with appropriate permissions.
- Routine System Status does not use the Master Key.
- Existing API keys are reconciled safely when required.
- The configured index is not confused with a verified existing index.
- Indexed document counts reflect actual Meilisearch statistics.
- Status failures are classified accurately.
- Search can be reported healthy even when statistics are unavailable.
- Indexing status does not depend on an administrator login.
- Credential rotation does not leave stale status clients.
- The Blazor UI refreshes accurately.
- Existing documents and indexes are preserved.
- Windows tests pass.
- Linux tests pass.
- English and Japanese documentation is updated.

**Do not consider the issue fixed merely because the UI displays "Healthy."**

The displayed status must be supported by verified backend operations.

---

## Required Completion Report

### A. Root cause

Identify the exact reason System Status reported:

```text
The search engine rejected the request.
```

Include the actual endpoint, credential role, HTTP status, and Meilisearch error code.

### B. Working update operation

Explain what the user was successfully updating and which backend processed that update.

### C. Credential changes

Describe any changes to role permissions, client configuration, provisioning, or rotation.

### D. Status architecture

Explain how connectivity, search readiness, indexing readiness, and statistics availability are now determined.

### E. Indexed document count

Identify the API and data source used for the displayed count.

### F. Files changed

List relevant code, tests, scripts, and documentation.

### G. Tests

Report commands actually executed and outcomes.

Distinguish:

- Passed.
- Failed.
- Skipped.
- Not executed.

### H. Windows/Linux verification

Report actual tests performed on each platform.

### I. Remaining limitations

Document unresolved status, permission, or synchronization issues.

---

## Final Instructions

Investigate why Lantern's System Status reports that Meilisearch rejected its request even though database updates appear to work.

Determine whether successful updates are SQLite operations, Meilisearch operations, or merely accepted asynchronous tasks.

Trace the failing status endpoint, HTTP response, credential role, and index scope.

Verify whether `indexes.get`, `stats.get`, or other necessary diagnostic permissions are missing.

Correct the status service and its credential configuration using the established least-privilege API key architecture.

Do not broaden Search or Indexing API Key permissions unnecessarily.

Separate engine connectivity, search readiness, indexing readiness, and statistics availability.

Display actual Meilisearch document counts instead of configuration values or SQLite record counts.

Ensure status refresh and credential rotation invalidate stale results and clients.

Preserve existing indexes, documents, crawler configuration, and credentials.

Add real authenticated Meilisearch integration tests and UI regression tests.

Verify behavior on Windows and Linux.

Update English and Japanese documentation.

**The primary success condition is that Lantern's System Status accurately reflects the real capabilities and state of the configured Meilisearch instance, including correct document statistics, without falsely reporting engine failure when only a restricted diagnostic request is denied.**
