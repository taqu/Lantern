# Lantern — Fix Meilisearch REST Embedder URI Rejection and Incorrect Indexing Success Reporting

## Objective

Investigate and permanently fix a mismatch between Lantern's indexing status and the actual Meilisearch indexing outcome.

Lantern appears to complete crawling and indexing submission successfully, while Meilisearch continues retrying failed REST embedding requests.

Observed Meilisearch logs:

```text
WARN milli::vector::embedder::rest:
Failed: runtime error: could not reach embedding server:
  - bad uri: Rejected URI

WARN milli::vector::embedder::rest:
Attempt #5, retrying after 40750ms.
```

The failure repeats across multiple embedding operations.

The implementation must fix:

1. The REST embedder URL rejection.
2. The repeated retry behavior caused by an invalid configuration.
3. Incorrect or premature indexing success reporting.
4. Missing end-to-end embedding readiness verification.
5. Recovery of outstanding or incomplete indexing tasks.

Do not merely suppress the warnings, extend task timeouts, or disable embedding.

## Phase 1 — Confirm the Runtime Configuration

Inspect the actual versions and startup configuration of:

- Meilisearch.
- LlamaPure embedding server.
- Lantern.Embedding.
- EmbedderSetupService.
- MeilisearchIndexingService.
- MeilisearchChunkIndexingService.
- FileSynchronizationService.
- Managed Meilisearch process launcher.

Determine the actual URL stored in Meilisearch's REST embedder settings.

Verify the configured endpoint, including:

- Scheme.
- Host.
- Port.
- Path.
- DNS resolution.
- Whether it resolves to IPv4 or IPv6 loopback.
- Whether the configured server is reachable from the Meilisearch process.

Do not confuse Meilisearch's API URL with the REST embedding endpoint URL.

## Phase 2 — Identify the URI Rejection

Investigate Meilisearch's network security restrictions.

The error `bad uri: Rejected URI` has been associated with the restriction of requests to private and loopback IP networks.

Reference:

https://github.com/meilisearch/meilisearch/issues/6223

Verify the exact installed Meilisearch version and supported network configuration options.

Check for the relevant version-supported configuration:

```text
MEILI_EXPERIMENTAL_ALLOWED_IP_NETWORKS
```

Older versions may use:

```text
MEILI_EXPERIMENTAL_ALLOW_IP_NETWORKS
```

Do not assume both names are supported.

Use the installed executable's `--help` output and version-specific documentation.

For a local IPv4 embedding server, evaluate a narrow allowance such as:

```text
127.0.0.0/8
```

If the actual endpoint uses IPv6 loopback, evaluate:

```text
::1/128
```

Do not use unrestricted network access (`any`) as the default solution.

Do not disable the SSRF protection globally.

Restrict who can modify embedder settings because allowed local destinations increase the security impact of privileged configuration access.

## Phase 3 — Correct Managed Meilisearch Startup

If Lantern starts Meilisearch as a child process:

- Apply the verified network configuration before the process starts.
- Pass it through `ProcessStartInfo.Environment` or the supported command-line option.
- Do not set permanent machine-wide environment variables.
- Ensure startup configuration is consistent across Windows and Linux.
- Verify the actual process received the setting.
- Confirm the effective setting after startup.

If Meilisearch is managed externally:

- Do not terminate or reconfigure the external process automatically.
- Provide a clear diagnostic and instructions for the administrator.

An environment variable added to Lantern's process after Meilisearch has already started will not automatically change the running Meilisearch process.

Preserve the existing database and indexes during any managed restart.

## Phase 4 — Validate the Embedder Configuration

Inspect the configured REST embedder settings.

Verify:

- The intended chunk index.
- REST embedder name.
- Endpoint URL.
- Request template.
- Response template.
- Document template.
- Model identity.
- Vector dimensions.
- Supported batching behavior.

Ensure that Lantern does not configure the embedder on an unintended parent index.

Confirm that the embedded text corresponds to actual extracted document chunks.

Avoid applying identical embedder settings repeatedly, because settings updates may initiate expensive indexing work.

Compare effective configuration before submitting changes.

## Phase 5 — Verify the Actual Network Path

Perform two distinct checks.

### Check A — Direct LlamaPure request

From Lantern, send a real embedding request to LlamaPure.

Verify:

- HTTP response.
- Request completion.
- Correct vector dimensions.
- Valid numeric values.
- Inference duration.

### Check B — Meilisearch-originated embedding request

Submit a small controlled indexing operation that requires Meilisearch to call its configured REST embedder.

Verify:

- LlamaPure receives the request.
- The request uses the expected endpoint.
- LlamaPure returns valid vectors.
- Meilisearch accepts the response.
- The indexing task reaches `succeeded`.

A successful request from Lantern to LlamaPure is insufficient evidence that Meilisearch can reach the same server.

Do not report embedding readiness until the second check succeeds.

## Phase 6 — Fix Retry and Failure Handling

Distinguish:

- Permanent configuration errors.
- Invalid or rejected URLs.
- Transient network failures.
- Embedding server timeouts.
- Invalid embedding responses.
- Model inference failures.
- Task queue delays.

An invalid or prohibited URI is a configuration problem.

Lantern must not indefinitely submit additional embedding-dependent operations when the effective configuration is known to be invalid.

Implement:

- Preflight validation.
- Actionable error reporting.
- Bounded recovery checks.
- Backpressure on new indexing submissions.
- Safe resumption after configuration repair.

Inspect whether the installed Meilisearch version has a fix for treating `Rejected URI` as non-retryable.

If necessary, evaluate upgrading to a compatible, supported Meilisearch release after verifying migration and data compatibility.

Do not attempt to modify Meilisearch's internal retry loop from Lantern unless a supported control is available.

## Phase 7 — Correct Lantern Indexing Completion Semantics

Audit all locations that report successful indexing.

Inspect:

- FileSynchronizationService.
- Parent document indexing.
- Chunk indexing.
- Task monitoring.
- SQLite synchronization state.
- Crawler progress reporting.
- System Status.

Ensure that Lantern distinguishes:

```text
Discovered
Extracted
IndexingSubmitted
IndexingPending
Indexed
IndexingFailed
IndexingOutcomeUnknown
```

Use the existing state model where possible.

### Critical requirement

Submitting a Meilisearch task successfully must not mark the document as fully indexed.

Only a verified successful final task outcome may establish completion.

If both parent and chunk indexing are required, define clearly whether overall document indexing success requires both.

Preserve task identifiers and outcomes across application restarts where needed.

Do not classify an indexing wait timeout as confirmed failure or success.

## Phase 8 — Recover Incomplete Tasks Safely

Inspect existing pending or timed-out Meilisearch tasks.

For each operation, preserve:

- Task UID.
- Index UID.
- Document identity.
- Document version.
- Operation type.
- Last known status.

After the underlying URI rejection is corrected:

1. Retrieve current task states.
2. Confirm which tasks succeeded.
3. Identify tasks that failed.
4. Identify tasks still processing.
5. Reconcile Lantern's SQLite state.
6. Retry only operations known to require resubmission.
7. Avoid duplicate or stale indexing updates.

Do not clear the Meilisearch data directory or recreate indexes to remove the retry backlog.

If a controlled cancellation of an irrecoverably blocked task is necessary, assess the affected index, task batch, and dependent operations first. Obtain authorization through the existing maintenance workflow and use the supported task cancellation API.

Do not cancel unrelated indexing work.

## Phase 9 — Correct System Status

Expose independent status for:

- Meilisearch connectivity.
- Parent index readiness.
- Chunk index readiness.
- Embedding server availability.
- Meilisearch-to-embedder connectivity.
- Embedding inference readiness.
- Pending indexing tasks.
- Failed indexing tasks.
- Hybrid Search readiness.

Do not report a healthy embedding system merely because LlamaPure is running.

Do not report successfully indexed documents based only on accepted tasks.

Preserve accurate status even when a task is pending for an extended period.

## Phase 10 — Regression Tests

Use real authenticated Meilisearch and the actual LlamaPure embedding backend.

Required scenarios:

1. Local embedding endpoint allowed by the configured network policy.
2. Local embedding endpoint rejected by the network policy.
3. Invalid REST endpoint URL.
4. Embedding server unavailable.
5. Embedding server returns malformed data.
6. Embedding inference succeeds.
7. Meilisearch indexing task succeeds.
8. Meilisearch indexing task remains pending.
9. Meilisearch indexing task fails.
10. Lantern does not prematurely report success.
11. Application restart reconciles pending tasks.
12. Repeated crawling avoids uncontrolled duplicate submissions.
13. Existing Keyword Search continues working.
14. Hybrid Search returns actual vector-backed results.
15. Windows and Linux process startup apply the correct network policy.
16. External Meilisearch deployments receive an actionable configuration diagnostic.

Verify that no repeated `Rejected URI` warnings occur during successful test indexing.

Do not consider warning suppression sufficient verification.

## Phase 11 — Documentation

Update English and Japanese developer and setup documentation.

Cover:

- Meilisearch REST embedder network restrictions.
- Correct version-specific environment variable names.
- Secure loopback network configuration.
- Managed and externally managed Meilisearch deployment differences.
- LlamaPure endpoint verification.
- Indexing task lifecycle.
- Recovery of incomplete tasks.
- Embedding readiness diagnostics.
- Security implications of allowing private network destinations.

## Acceptance Criteria

The issue is resolved when:

- The rejected REST embedder URL is identified.
- The actual reason for rejection is verified.
- The correct network policy is applied to the running Meilisearch instance.
- Meilisearch can call LlamaPure successfully.
- Embedding vectors are generated and accepted.
- No invalid-URI retry loop occurs during normal operation.
- Meilisearch indexing tasks reach verified terminal states.
- Lantern does not report successful indexing prematurely.
- Existing pending tasks are reconciled safely.
- Keyword Search remains functional.
- Hybrid Search returns actual vector-backed results.
- Existing indexes, documents, and credentials are preserved.
- Windows and Linux support is verified where available.

## Required Completion Report

Provide:

1. Actual Meilisearch version.
2. Configured embedder URL.
3. Root cause of the URI rejection.
4. Verified network policy and configuration option.
5. Effective runtime configuration.
6. Evidence of real Meilisearch-to-LlamaPure requests.
7. Before-and-after task outcomes.
8. Changes to Lantern's indexing success criteria.
9. Recovery actions for existing pending tasks.
10. Source files changed.
11. Actual test commands and outcomes.
12. Remaining limitations.

## Final Instructions

Fix the root cause of `bad uri: Rejected URI` before making further timeout adjustments.

Ensure the Meilisearch process permits only the intended local embedding endpoint.

Verify the actual REST embedding request from Meilisearch to LlamaPure.

Stop incorrectly reporting accepted indexing tasks as completed indexing operations.

Prevent additional embedding-dependent submissions when the configuration is known to be invalid.

Reconcile outstanding tasks safely without destructive index resets.

**Success requires a functioning Meilisearch-to-LlamaPure embedding path, completed indexing tasks, and consistent operational status between Lantern and Meilisearch.**
