# Lantern — Fix Hybrid Search Requiring Administrator Authentication

## Objective

Investigate and fix Lantern's Hybrid Search authorization and execution problems.

Keyword Search is now working correctly, but Hybrid Search does not function as expected.

The user suspects that Hybrid Search requires administrator privileges.

The observed logs are:

```text
GET /search?q=パフォーマンス&page=1&mode=hybrid

Cookies was not authenticated.
Failure message: Ticket expired

Executing endpoint '/search (/search)'
Executed endpoint '/search (/search)'

Request finished ... 200 ... 27.1800ms
```

These logs indicate that the Lantern authentication cookie expired. They do not prove that Meilisearch rejected the Hybrid Search request.

The initial HTTP response completed successfully, but the actual interactive search operation may still have failed.

**Your task is to identify the exact failure, separate user authentication from Meilisearch API authorization, and make Hybrid Search work without requiring Lantern administrator login.**

Preserve the existing role-based security architecture.

Do not weaken authorization for administrative operations.

---

## Phase 1 — Reproduce and Identify the Failure

### 1. Reproduce the Reported Scenario

Use:

```text
https://localhost:7108/search?q=パフォーマンス&page=1&mode=hybrid
```

Test under three conditions:

1. Anonymous user with no authentication cookie.
2. User with an expired authentication cookie.
3. Authenticated Lantern administrator.

For each case, record:

- Whether the page loads.
- Whether the interactive Blazor circuit initializes.
- Whether Hybrid Search executes.
- Whether results are returned.
- Whether a login redirect occurs.
- Whether any backend request returns HTTP 401 or 403.
- Whether an exception is thrown.
- Whether the embedding backend is contacted.

Do not interpret the initial HTTP 200 response as proof that Hybrid Search succeeded.

### 2. Inspect Browser and Server Diagnostics

Inspect:

- Browser Network tab.
- Browser console.
- Initial document request.
- Blazor Server circuit activity.
- Search service logs.
- Meilisearch API requests.
- Embedding server logs.
- Authorization failures.
- Component rendering errors.

Identify the exact stage at which Hybrid Search fails.

Do not assume the expired cookie is the root cause without demonstrating that authentication state changes the outcome.

### 3. Compare Keyword and Hybrid Search

Execute both modes using the same query:

```text
Keyword:
  /search?q=パフォーマンス&page=1&mode=keyword

Hybrid:
  /search?q=パフォーマンス&page=1&mode=hybrid
```

Compare:

- Razor component execution.
- Search service methods.
- Authorization policies.
- Meilisearch client selection.
- Credential acquisition.
- Query construction.
- Index selection.
- Result mapping.
- Error handling.

Determine the exact point where the execution paths diverge.

---

## Phase 2 — Audit ASP.NET Core Authorization

### 4. Inspect Search Page Authorization

Locate the `/search` Razor component and its route configuration.

Inspect:

- `[Authorize]` attributes.
- `[AllowAnonymous]` attributes.
- Role-based authorization.
- Policy-based authorization.
- Parent component authorization.
- `AuthorizeRouteView`.
- Middleware configuration.
- Endpoint conventions.
- Fallback authorization policies.
- Any custom search mode authorization checks.

Determine whether Hybrid Search is incorrectly routed through an administrator-only operation.

### Required behavior

Follow the existing application policy for public or anonymous search.

If Keyword Search is available anonymously, Hybrid Search should ordinarily follow the same access policy.

Do not create an administrator requirement solely because the search mode uses embeddings.

### 5. Inspect Search Mode Authorization

Search for code equivalent to:

```csharp
if (mode == SearchMode.Hybrid)
{
    RequireAdministrator();
}
```

or:

```csharp
[Authorize(Roles = "Administrator")]
public Task<SearchResult> HybridSearchAsync(...)
```

Also inspect indirect authorization dependencies.

For example:

```text
Hybrid Search
    -> Embedder Configuration Service
    -> Admin Authorization
    -> Failure
```

Determine whether an administrator-only service is being invoked during ordinary search.

### Important

Do not simply remove authorization attributes from shared administrative services.

Separate read-only search execution from privileged configuration management.

---

## Phase 3 — Separate Search Execution from Administration

### 6. Review Meilisearch Client Roles

Inspect the existing Meilisearch client factory.

Identify the intended clients:

```text
Search Client
Indexing Client
Admin Client
Internal Maintenance Client
```

Determine which client Hybrid Search currently uses.

Expected behavior:

```text
Keyword Search
    -> Search Client

Hybrid Search
    -> Search Client

Embedder Configuration
    -> Authorized Maintenance/Admin Client
```

Hybrid Search must not require a Meilisearch Admin API Key merely to execute a query.

### 7. Eliminate Administrative Operations from Search Execution

Inspect whether Hybrid Search performs any of the following during each query:

- Reading or modifying index settings.
- Creating the index.
- Configuring an embedder.
- Updating embedder settings.
- Provisioning API keys.
- Rotating credentials.
- Starting indexing jobs.
- Changing searchable attributes.
- Reading administrative configuration unnecessarily.

Identify which operations are genuinely required for searching.

Move initialization and configuration operations into the appropriate application startup, provisioning, or maintenance service.

### Important

Do not move privileged operations into an anonymous endpoint.

Do not use the Master Key for ordinary Hybrid Search.

Do not temporarily elevate the user to an administrator.

### 8. Verify the Search API Key

Using the actual configured Search API Key, verify that an authorized Hybrid Search request can execute against the intended index.

Check:

- API Key validity.
- Expiration.
- Index scope.
- `search` permission.
- Meilisearch version compatibility.
- Hybrid query parameters.

If the request fails, capture the actual HTTP status and Meilisearch error code.

Do not grant `*` permissions as a workaround.

---

## Phase 4 — Verify the Hybrid Search Pipeline

### 9. Inspect Hybrid Query Construction

Determine how Lantern builds the Meilisearch request.

For a configured embedder, a typical Hybrid Search request resembles:

```http
POST /indexes/lantern-dev-documents/search
Content-Type: application/json
Authorization: Bearer <search-api-key>
```

```json
{
  "q": "パフォーマンス",
  "hybrid": {
    "embedder": "<configured-embedder-name>",
    "semanticRatio": 0.5
  },
  "limit": 20
}
```

This is illustrative. Use the actual configured index, embedder, and search settings.

Verify:

- Correct index UID.
- Correct embedder name.
- Correct semantic ratio.
- Correct filters.
- Correct pagination.
- Correct Search API Key.
- Correct request serialization.
- Compatible Meilisearch API version.

### 10. Verify Embedder Initialization

Inspect the established embedding architecture.

Lantern uses a local embedding service integrated with Meilisearch.

Determine whether the selected index already has a valid embedder configuration.

Check:

- Embedder name.
- Embedder source type.
- REST endpoint configuration.
- Request and response templates.
- Vector dimensions.
- Actual embedding server readiness.
- Indexing task status.

Do not configure or overwrite the embedder on every search.

If the embedder is missing, report a configuration problem rather than requesting Lantern administrator login from the search user.

### 11. Verify Existing Embeddings

Determine whether the target documents actually contain searchable vectors.

Check:

- Document indexing status.
- Chunk indexing status, if implemented.
- Vector generation status.
- Failed embedding tasks.
- Actual vector dimensions.
- Compatibility with the configured embedder.

Distinguish:

1. Hybrid Search rejected due to authorization.
2. Hybrid Search rejected due to invalid embedder configuration.
3. Hybrid Search failing because the embedding server is unavailable.
4. Hybrid Search returning zero results because vectors are unavailable.
5. Hybrid Search succeeding but results being lost during Lantern result mapping.

Do not classify all failures as permission errors.

### 12. Verify the Embedding REST Server

If Hybrid Search uses the LlamaPure embedding server, verify:

- Process status.
- REST endpoint availability.
- Request format.
- Response format.
- Embedding model loading.
- Response dimensions.
- Request timeout.
- Actual Japanese query embedding.
- Meilisearch-to-embedder connectivity.

For the project's configured Granite embedding model, verify the actual dimensions from the model and endpoint rather than hardcoding assumptions.

Do not require Lantern administrator authentication for local embedding inference.

---

## Phase 5 — Correct Cookie Authentication Behavior

### 13. Handle Expired Cookies Properly

The observed message is:

```text
Cookies was not authenticated.
Failure message: Ticket expired
```

Determine whether this is expected for an anonymously accessible page.

An expired cookie should not prevent anonymous search if the application allows anonymous Keyword Search.

Verify:

- Anonymous route access.
- Authentication middleware behavior.
- Authorization policies.
- Blazor circuit authentication state.
- Cached authentication state.
- Redirect behavior.

Do not disable Cookie Authentication globally.

Do not extend cookie expiration indefinitely as a workaround.

Do not disable administrator authentication on administrative pages.

### 14. Separate User Authentication from Backend Credentials

There must be a clear distinction between:

**Lantern user authentication**

Controls access to protected Lantern features.

**Meilisearch Search API Key**

Authorizes server-side search operations.

**Meilisearch Admin/Maintenance credentials**

Authorize privileged index and configuration operations.

Normal search should use the server-managed Search API Key according to Lantern's existing access policy.

The browser must never receive the Meilisearch Master Key or Admin API Key.

---

## Phase 6 — Fix Hybrid Search Error Handling

### 15. Preserve Accurate Errors

Avoid replacing every Hybrid Search failure with generic messages such as:

```text
Administrator privileges required.
```

Distinguish:

```text
UserAuthenticationRequired
UserAuthorizationDenied
SearchApiKeyInvalid
MeilisearchPermissionDenied
EmbedderNotConfigured
EmbeddingServerUnavailable
EmbeddingGenerationFailed
HybridQueryInvalid
IndexNotFound
NoMatchingDocuments
```

Adapt these to the existing error model.

Record actual backend error codes.

Do not expose internal credentials or sensitive configuration.

### 16. Do Not Silently Fall Back to Keyword Search

If Hybrid Search is unavailable, display an accurate diagnostic.

Do not silently execute Keyword Search while labeling the results as Hybrid Search.

If a fallback mechanism is desired, it must be explicit and identifiable.

The normal success condition is an actual Hybrid Search request that completes successfully.

---

## Phase 7 — Regression Tests

### 17. Authentication Matrix

Test all combinations:

| User state | Keyword | Hybrid |
|---|---|---|
| Anonymous | Works according to existing policy | Same access policy |
| Expired cookie | Works according to existing policy | Same access policy |
| Authenticated administrator | Works | Works |

Verify that administrative configuration endpoints remain protected.

### 18. Meilisearch Permission Tests

Use real authenticated Meilisearch.

Verify:

1. Search API Key performs Keyword Search.
2. Search API Key performs Hybrid Search with a configured embedder.
3. Search API Key cannot modify embedder settings.
4. Search API Key cannot create API keys.
5. Search API Key cannot modify unrelated indexes.
6. Admin/Maintenance credentials are used only for their authorized operations.
7. Invalid credentials produce accurate diagnostics.

### 19. Hybrid Search Integration Tests

With a real embedding backend:

1. Configure a test embedder through the authorized maintenance path.
2. Index test documents.
3. Wait for indexing and embedding tasks to complete.
4. Verify vectors are available.
5. Execute Keyword Search.
6. Execute Hybrid Search.
7. Verify Hybrid Search returns actual results.
8. Verify the operation uses the Search API Key.
9. Verify anonymous search works according to the application policy.
10. Verify expired cookies do not change the result.
11. Verify no administrative operation occurs during normal search.

Include Japanese queries.

### 20. Failure Scenarios

Test:

- Embedder missing.
- Embedding server unavailable.
- Invalid embedder name.
- Incorrect vector dimensions.
- No indexed vectors.
- Invalid Search API Key.
- Index permission mismatch.
- Expired authentication cookie.
- Backend timeout.
- Invalid Hybrid Search request.

Each case must produce a meaningful result or error.

---

## Phase 8 — Preserve Existing Features

Ensure the fix does not regress:

- Keyword Search.
- Search pagination.
- Category filtering.
- Parent/chunk result aggregation.
- File path copying.
- Directory path copying.
- URL copying.
- Crawler operations.
- Document indexing.
- Meilisearch credential management.
- Administrative access control.

Do not recreate indexes or delete documents.

Do not regenerate the Master Key.

Do not reset the administrator password.

Do not require a full reindex unless a separately verified embedding migration makes one necessary.

---

## Phase 9 — Documentation

Update relevant English and Japanese documentation.

Describe:

- Keyword Search authorization.
- Hybrid Search authorization.
- Search API Key requirements.
- Embedder setup ownership.
- Embedding backend dependencies.
- Authentication versus API authorization.
- Expected behavior for anonymous users.
- Hybrid Search troubleshooting.

Do not describe admin authentication as a requirement for executing Hybrid Search unless an explicit product policy independently requires it.

---

## Acceptance Criteria

The task is complete when:

- The exact Hybrid Search failure is identified.
- Expired authentication cookies are handled correctly.
- Anonymous Hybrid Search follows the same access policy as Keyword Search.
- Hybrid Search uses the server-managed Search API Key.
- No administrative credentials are required for ordinary searches.
- Embedder configuration is not modified during search execution.
- The configured embedding backend works.
- Meilisearch accepts actual Hybrid Search requests.
- Hybrid Search returns relevant results for indexed test documents.
- Hybrid failures are not misreported as permission failures.
- Keyword Search remains functional.
- Administrative endpoints remain protected.
- Clipboard functionality remains operational.
- Windows and Linux tests pass where the environments are available.
- English and Japanese documentation reflects the actual behavior.

---

## Required Completion Report

### A. Root Cause

Identify the exact cause of Hybrid Search failure.

State whether it was:

- Lantern user authentication.
- Lantern authorization policy.
- Meilisearch API Key permissions.
- Accidental administrative service dependency.
- Embedder configuration.
- Embedding backend.
- Query construction.
- Search result mapping.

### B. Authentication Analysis

Explain why the expired Cookie Authentication ticket appeared and whether it affected the search.

### C. Authorization Architecture

Document the final separation of search and administrative privileges.

### D. Meilisearch Verification

Report the actual Hybrid Search request status and any backend error codes.

### E. Embedding Verification

Report actual embedder configuration, embedding generation, and indexing readiness.

### F. Files Changed

List source files, tests, configuration, and documentation.

### G. Tests

Report actual test commands and outcomes.

Distinguish passed, failed, skipped, and unexecuted tests.

### H. Remaining Limitations

Describe unresolved issues without claiming unsupported functionality.

---

## Final Instructions

Investigate and fix Lantern's Hybrid Search failure.

Keyword Search works, but Hybrid Search does not.

The logs show an expired ASP.NET Core authentication cookie and an HTTP 200 response for the initial `/search` request.

Do not assume these logs prove that Meilisearch requires administrator privileges.

Trace the actual interactive search operation, including ASP.NET Core authorization, Meilisearch client selection, search API permissions, embedder configuration, and embedding backend execution.

Ensure that ordinary Hybrid Search uses the same application access policy as Keyword Search and the appropriate server-managed Search API Key.

Keep administrative configuration operations separate and protected.

Do not expose privileged credentials to users or browsers.

Do not silently fall back to Keyword Search.

Add real end-to-end tests using authenticated Meilisearch and the configured embedding service.

Preserve existing documents, credentials, search behavior, and clipboard functionality.

**Success means that users permitted to run Keyword Search can also run actual Hybrid Search without administrator privileges, while Meilisearch administrative operations remain properly protected.**
