# Lantern — Fix Embedder Initialization and Chunk Index 404 Errors

## Confirmed Failure

During Lantern startup, `EmbedderSetupService` fails before completing Meilisearch embedder configuration.

The exception is:

```text
HttpRequestException: Response status code does not indicate success:
404 (Not Found)
```

Stack trace:

```text
Meilisearch.Index.GetStatsAsync()
  -> MeilisearchChunkIndexingService.EnsureCompatiblePrimaryKeyAsync()
  -> MeilisearchChunkIndexingService.ConfigureAsync()
  -> MeilisearchChunkIndexingService.EnsureIndexAsync()
  -> EmbedderSetupService.RunAsync()
```

The failure is reported as:

`Failed to configure Meilisearch embedder for hybrid search.`

Related problems have also occurred in `MeilisearchIndexingService`, including incorrect handling of existing indexes with unset primary keys.

Keyword Search currently works, but Hybrid Search and embedding indexing have not been successfully verified.

## Objective

Fix index initialization, primary key validation, and embedder setup so that Lantern can reliably configure and use its embedding pipeline.

Do not assume that the embedding model itself is defective.

## 1. Find the Exact Cause of HTTP 404

Inspect:

- `MeilisearchChunkIndexingService.cs`
- `MeilisearchIndexingService.cs`
- `EmbedderSetupService.cs`
- Meilisearch client factory
- Index configuration
- Credential management

Capture the complete HTTP 404 response, including its Meilisearch error code.

Determine whether:

- The chunk index does not exist.
- An index creation task has not completed.
- Index creation previously failed.
- The wrong index UID is used.
- The wrong Meilisearch instance is selected.
- The client lacks required permissions.
- The SDK is accessing an incorrect endpoint.

Do not treat every 404 as proof that an index is missing.

## 2. Correct Index Creation and Verification

Implement a shared, idempotent index initialization strategy for parent and chunk indexes.

Required behavior:

1. Request actual index metadata.
2. If the index exists, verify its primary key.
3. If it does not exist, create it with the intended primary key.
4. Wait for the index creation task to succeed.
5. Retrieve the index metadata again.
6. Verify the resulting primary key.
7. Apply required settings.
8. Wait for settings tasks to succeed.

Handle concurrent initialization safely.

Do not delete or recreate existing indexes.

## 3. Correct Primary Key Validation

Inspect `EnsureCompatiblePrimaryKeyAsync()`.

Do not call `GetStatsAsync()` merely to obtain the primary key.

Use actual index metadata.

If the primary key is unset:

- Determine whether the index is empty.
- Assign the expected primary key only when permitted and safe.
- Wait for the update task.
- Verify the result.

If statistics are required to determine whether an unset-primary-key index is empty, perform that call only after the index has been confirmed to exist.

Never interpret a failed statistics request as a document count of zero.

Do not treat an unset primary key as a genuinely incompatible nonempty primary key.

## 4. Verify Credential Roles

Identify the credentials used by:

- Index creation.
- Index metadata retrieval.
- Index statistics.
- Primary key updates.
- Embedder settings.
- Chunk indexing.
- Hybrid Search.

Administrative initialization must use an appropriate authorized maintenance credential.

Normal Hybrid Search must use the Search API Key and must not require administrator login.

Do not grant broad permissions as a workaround.

## 5. Separate Initialization Stages

Refactor the startup workflow where necessary so that it distinguishes:

- Meilisearch connectivity.
- Parent index readiness.
- Chunk index readiness.
- Primary key compatibility.
- Embedder configuration.
- Embedding server readiness.
- Vector indexing readiness.
- Hybrid Search readiness.

Do not report a generic embedder configuration error when the actual failure occurs during chunk index creation.

Ensure initialization failures remain observable and retryable without starting uncontrolled background retry loops.

## 6. Verify the Embedding Architecture

Inspect `Lantern.Embedding` and determine whether Lantern uses:

- Meilisearch REST embedder, or
- Lantern-generated `userProvided` vectors.

Ensure one coherent approach is implemented.

Do not add redundant direct embedding calls merely because `Lantern.Embedding` is not referenced by the crawler.

If the REST embedder is selected, verify actual Meilisearch-to-LlamaPure inference requests.

If `userProvided` is selected, verify explicit vector generation and submission.

## 7. Verify End-to-End Operation

After fixing initialization:

1. Confirm the parent index exists with the expected primary key.
2. Confirm the chunk index exists with the expected primary key.
3. Confirm embedder settings are installed on the intended index.
4. Verify a real Japanese embedding inference request.
5. Crawl a controlled test document.
6. Verify extracted chunks.
7. Verify successful Meilisearch indexing tasks.
8. Verify the required vectors exist or can be generated.
9. Execute a real Hybrid Search.
10. Verify nonempty results for known relevant test content.
11. Confirm Keyword Search still works.
12. Confirm no administrator login is required for normal searching.

## 8. Regression Tests

Add integration tests using real authenticated Meilisearch for:

- Missing indexes.
- Existing compatible indexes.
- Unset primary keys.
- Incompatible primary keys.
- Concurrent index initialization.
- Pending index creation tasks.
- Failed index creation tasks.
- Missing statistics permissions.
- Successful embedder configuration.
- Failed embedder configuration.
- Application restart.
- Embedding enabled and disabled.
- Real chunk indexing.
- Real Hybrid Search.

Verify on Windows and Linux where the environments are available.

## Acceptance Criteria

- The 404 from `GetStatsAsync()` is explained and corrected.
- Index creation tasks are awaited and verified.
- Primary key validation uses the correct metadata.
- Parent and chunk index initialization is idempotent.
- Existing indexed documents remain intact.
- Embedder setup completes successfully.
- Embedding inference is verified.
- Chunk indexing succeeds.
- Hybrid Search returns real results.
- Keyword Search remains functional.
- No destructive index reset is required.

## Completion Report

Report:

- Exact cause of the HTTP 404.
- The affected chunk index UID.
- Actual Meilisearch error code.
- Primary key before and after initialization.
- Changes to `MeilisearchChunkIndexingService`.
- Changes to `MeilisearchIndexingService`.
- Changes to `EmbedderSetupService`.
- Actual embedder configuration.
- Embedding inference test results.
- Chunk indexing task results.
- Hybrid Search test results.
- Windows/Linux test outcomes.
- Remaining limitations.

**The fix is complete only when Lantern successfully initializes both index structures, configures the intended embedder, indexes chunks with working vectors, and executes Hybrid Search without requiring administrative privileges from ordinary users.**
