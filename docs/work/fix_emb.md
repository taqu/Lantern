# Lantern — Fix REST Embedder URI Rejection and Repeated Indexing Retries

## Implementation Status (2026-11-23)

### Phase 1 — Verified Configuration
- Meilisearch version: **1.57.0**
- REST embedder URL: `http://127.0.0.1:7701/embed` (from `EmbeddingOptions.ListenAddress` + `Port`)
- LlamaPure server: `http://127.0.0.1:7701` (port 7703 in Development)
- API format confirmed correct: `{"text":"..."}` → `{"embedding":[float×384]}`
- `--experimental-allow-ip-networks` was **not** being passed at process launch

### Phase 2 — Network Policy Identified
- Exact flag in Meilisearch 1.57.0: `--experimental-allowed-ip-networks` (note: **allowed**, not **allow**)
- Env var: `MEILI_EXPERIMENTAL_ALLOWED_IP_NETWORKS`
- Recommended value: `127.0.0.0/8`

### Phase 3 — Completed
- Added `AllowedIpNetworks` property to `MeilisearchProcessOptions` (default `127.0.0.0/8`)
- `MeilisearchProcessManager` now sets `MEILI_EXPERIMENTAL_ALLOWED_IP_NETWORKS` via `ProcessStartInfo.Environment`
- Covers all managed-process deployments (dev, Windows, Windows Service, Linux, systemd)
- External Meilisearch operators must configure the same env var independently

### Phase 4 — Completed
- `EmbedderSetupService` now calls `VerifyEmbeddingServerAsync` before `ConfigureEmbedderAsync`
- Preflight POSTs a probe request to `/embed`, validates 384-dimension response
- If the embedding server is unreachable or returns incorrect dimensions, embedder is not registered
- Prevents Meilisearch from being pointed at a non-functional endpoint

### Phase 5 — Verified Correct
- `EmbedderConfigurationService` request/response templates match LlamaPure API
- `Dimensions = 384` consistent across embedder config and preflight check

### Phase 9 — Partial
- Added `MeilisearchProcessOptionsTests` (3 tests for `AllowedIpNetworks` defaults)
- Full integration tests (live Meilisearch + LlamaPure) pending Phase 8 environment setup

---



## Confirmed Problem

When embedding is enabled, Lantern's crawler submits documents to Meilisearch, but indexing does not complete.

Meilisearch repeatedly logs:

```text
WARN milli::vector::embedder::rest:
Attempt #9, retrying after 58682ms.

Failed: runtime error: could not reach embedding server:
  - bad uri: Rejected URI
```

Meanwhile, Lantern repeatedly polls Meilisearch tasks, and document indexing times out after 60 seconds.

### Important finding

This error is consistent with Meilisearch's security restrictions on REST embedder connections to loopback, private, or otherwise disallowed network addresses.

Meilisearch issue #6223 documents the same `Rejected URI` behavior and excessive retries.

Reference:
https://github.com/meilisearch/meilisearch/issues/6223

The primary objective is to make the local embedding endpoint accessible under an explicitly configured, restricted Meilisearch network policy.

Do not treat this as a generic embedding performance problem.

## Phase 1 — Verify the Exact Runtime Configuration

Inspect:

- Meilisearch version and startup arguments.
- Meilisearch process launcher.
- `EmbedderSetupService`.
- `Lantern.Embedding`.
- LlamaPure embedding server configuration.
- REST embedder settings stored in Meilisearch.
- Windows and Linux startup scripts.
- Windows Service and systemd configurations.

Determine the actual REST embedder URL registered in Meilisearch.

Do not infer it from the Meilisearch API address.

Verify whether it uses:

- `127.0.0.1`
- `localhost`
- `::1`
- A private network IP address.
- A local DNS hostname.

Capture the exact endpoint and configuration source without exposing credentials.

## Phase 2 — Identify the Network Policy Rejection

Verify whether Meilisearch rejects the endpoint because of its IP network allowlist or another URL validation rule.

Check the supported configuration options for the exact installed Meilisearch version.

Investigate:

```text
--experimental-allow-ip-networks
MEILI_EXPERIMENTAL_ALLOW_IP_NETWORKS
```

Do not assume the environment variable name, syntax, or behavior is identical across all Meilisearch versions.

Inspect the installed binary's supported options and authoritative version-specific documentation.

### Security requirement

For Lantern's local embedding architecture, prefer narrowly scoped loopback access.

For example, if supported:

```text
127.0.0.0/8
```

If IPv6 loopback is used, investigate:

```text
::1/128
```

Do not configure unrestricted network access (`any`) by default.

Do not disable Meilisearch SSRF protections globally.

Do not allow the browser or arbitrary users to modify REST embedder URLs.

## Phase 3 — Fix Meilisearch Process Startup

Lantern should configure the network policy when launching its managed Meilisearch process.

Use `ProcessStartInfo.Environment` or supported command-line arguments.

Do not require users to set permanent system-wide environment variables.

Ensure the configuration is applied consistently in:

- Visual Studio development startup.
- Normal Windows execution.
- Windows Service deployment.
- Linux execution.
- systemd deployment.

Verify whether the Meilisearch instance is managed by Lantern or externally administered.

For externally managed instances, document the required configuration rather than attempting to modify an unrelated process.

Do not restart or terminate a Meilisearch instance that Lantern does not own.

## Phase 4 — Validate the Embedding Endpoint Before Indexing

Introduce a preflight validation step before enabling embedding-dependent crawling.

Verify:

1. The embedding endpoint URL is syntactically valid.
2. Its destination is permitted by the configured Meilisearch network policy.
3. The LlamaPure embedding server is running.
4. The model is loaded.
5. A real embedding inference request succeeds.
6. The response contains valid numeric vectors.
7. Vector dimensions match the selected model.
8. Meilisearch can invoke the configured REST embedder successfully.

### Critical distinction

A successful HTTP request from Lantern to LlamaPure does not prove that Meilisearch can reach the same endpoint.

Verify the Meilisearch-to-embedding-server path separately.

Avoid embedding a test request on every crawler iteration.

Use startup validation and explicit readiness/recovery checks.

## Phase 5 — Correct Embedder Setup

Inspect the current embedder configuration produced by `EmbedderSetupService`.

Validate:

- `source`
- `url`
- `request`
- `response`
- `documentTemplate`
- `dimensions`, where applicable
- Model identity

Verify that the REST request and response templates match the actual LlamaPure embedding API.

Do not submit an invalid or unverified configuration.

Do not reconfigure the embedder during every crawl or Hybrid Search operation.

Do not repeatedly submit identical settings-update tasks.

Preserve existing index settings and embeddings unless an explicit, verified migration is required.

## Phase 6 — Prevent Unbounded Retry Behavior

Determine which component owns each retry:

- Meilisearch REST embedder internal retries.
- Lantern indexing task polling.
- Lantern indexing task resubmission.
- Crawler retry scheduling.

The log's `Attempt #9` originates from Meilisearch, not necessarily from a Lantern crawler loop.

### Required behavior

If embedding configuration is permanently invalid:

- Mark embedding readiness as failed.
- Stop submitting new embedding-dependent indexing work.
- Report the exact configuration problem.
- Keep Keyword Search available.
- Do not restart the embedding server repeatedly without evidence.
- Do not repeatedly reapply the same invalid embedder configuration.

Investigate version-supported Meilisearch retry controls or fixes where necessary.

Do not attempt to suppress Meilisearch's internal retries merely by increasing Lantern's task timeout.

Do not require uncontrolled process restarts to recover.

## Phase 7 — Repair Indexing Task State Management

Inspect:

- `MeilisearchIndexingService`
- `MeilisearchChunkIndexingService`
- `FileSynchronizationService`

When a Meilisearch task exceeds Lantern's completion deadline:

- Preserve the task UID.
- Preserve the affected document ID.
- Distinguish timeout from confirmed task failure.
- Reconcile the actual task state later.
- Do not blindly resubmit the same document.
- Prevent duplicate or stale updates.

A client-side timeout must not be interpreted as cancellation of a Meilisearch task.

Keep existing document and chunk data intact.

## Phase 8 — Verify End-to-End Embedding

After correcting the network policy and REST embedder configuration:

1. Start Meilisearch with the verified restricted network policy.
2. Start the LlamaPure embedding server.
3. Verify direct embedding inference.
4. Verify actual Meilisearch-to-LlamaPure inference.
5. Verify that index and embedder setup tasks succeed.
6. Crawl one small controlled document.
7. Confirm parent document indexing succeeds.
8. Confirm chunk indexing succeeds.
9. Confirm actual embeddings are generated.
10. Execute a real Hybrid Search with a Japanese query.
11. Verify the returned results.
12. Confirm Keyword Search remains functional.

Include the query:

```text
パフォーマンス計測
```

Test with known relevant indexed content.

Do not classify the pipeline as successful based on HTTP 200 responses alone.

## Phase 9 — Regression Tests

Add automated tests for:

- Valid loopback REST embedder URL.
- Invalid embedder URL.
- Restricted network policy.
- Missing embedding server.
- Incorrect response schema.
- Incorrect vector dimensions.
- Embedding service startup failure.
- Crawler behavior when embedding is unavailable.
- Successful recovery after configuration correction.
- No redundant embedder configuration updates.
- No repeated submissions after unknown task outcomes.
- Normal Keyword Search with embedding unavailable.
- Actual Hybrid Search after recovery.

Test on Windows and Linux where environments are available.

Use real Meilisearch and LlamaPure for integration verification.

## Phase 10 — Documentation

Update English and Japanese documentation.

Explain:

- Why Meilisearch may reject local embedding endpoints.
- The network security implications.
- Supported loopback allowlist configuration.
- How Lantern passes configuration to managed Meilisearch.
- How external Meilisearch deployments must be configured.
- How to diagnose `bad uri: Rejected URI`.
- How to verify real embedding inference.
- How to recover incomplete indexing tasks.

Do not recommend unrestricted IP network access as the standard setup.

## Acceptance Criteria

The issue is resolved when:

- The exact rejected REST embedder URL is identified.
- The rejection cause is verified.
- Meilisearch permits only the intended embedding endpoint network.
- No repeated `Rejected URI` retries occur during valid crawling.
- The embedding server receives actual inference requests.
- Embedding inference succeeds.
- Meilisearch indexing tasks complete successfully.
- Crawler processing no longer stalls behind the invalid configuration.
- Failed or pending indexing tasks remain recoverable.
- Keyword Search continues to work.
- Hybrid Search returns actual vector-backed results.
- Windows/Linux configuration is supported.
- Existing indexes, documents, and credentials are preserved.

## Required Completion Report

Report:

1. Meilisearch version.
2. Actual rejected embedder URL.
3. Root cause of `Rejected URI`.
4. Exact network policy applied.
5. Meilisearch startup changes.
6. Embedding endpoint verification.
7. Meilisearch task outcomes before and after.
8. Crawler test results.
9. Hybrid Search test results.
10. Remaining limitations.

**The primary success condition is a secure, operational Meilisearch-to-LlamaPure connection that allows embedding-enabled crawling and Hybrid Search without repeated URI rejection or uncontrolled task retries.**
