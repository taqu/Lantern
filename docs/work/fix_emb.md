# Lantern — Fix Persistent `Rejected URI` in Meilisearch REST Embedding

## Objective

Fix the persistent failure of Meilisearch to contact Lantern's local LlamaPure embedding server.

The current failure occurs during Hybrid Search:

```text
POST /indexes/lantern-dev-chunks/search
HTTP 400 vector_embedding_error

Error while generating embeddings:
  could not reach embedding server:
  bad uri: Rejected URI
```

Meilisearch repeatedly retries the rejected request.

This strongly indicates an outbound REST embedder URL policy rejection, not an ordinary search API connectivity failure.

Do not modify the model inference implementation until this connectivity problem has been resolved or disproved.

## 1. Identify the Actual Runtime Configuration

Inspect the following:

- Meilisearch version and executable path.
- Meilisearch process ownership and startup code.
- Meilisearch process arguments and environment configuration.
- Effective REST embedder settings for `lantern-granite`.
- LlamaPure embedding endpoint and listener address.
- `EmbedderSetupService`.
- Managed and external Meilisearch configuration.
- Windows and Linux startup scripts.

Determine the actual embedding server URL used by Meilisearch.

Do not infer it from the Meilisearch API URL.

## 2. Verify the IP Network Policy

Inspect the actual Meilisearch executable's help output and its supported options.

Check the version-appropriate equivalent of:

```text
--experimental-allowed-ip-networks
MEILI_EXPERIMENTAL_ALLOWED_IP_NETWORKS
```

Check for version-specific differences rather than assuming one spelling works universally.

Meilisearch rejects outbound requests to non-global destinations unless permitted by its supported configuration.

For a verified IPv4 loopback endpoint, prefer a restricted setting such as:

```text
127.0.0.0/8
```

For IPv6 loopback, verify support for:

```text
::1/128
```

Do not use `any` by default.

Do not disable SSRF protections globally.

Use the narrowest supported network allowance that permits the actual embedding endpoint.

## 3. Fix the Meilisearch Process Launcher

If Lantern manages Meilisearch:

- Resolve the intended embedding server address before launching Meilisearch.
- Configure the required network policy on the Meilisearch child process.
- Use `ProcessStartInfo.Environment` or verified supported CLI arguments.
- Do not modify global or machine-wide environment variables.
- Log the effective non-secret startup policy.
- Verify that the process receiving the policy is the process serving port 7700.
- Handle already-running Meilisearch instances explicitly.
- Do not spawn a second Meilisearch instance to work around a misconfigured first instance.

If Meilisearch is externally managed:

- Detect that Lantern does not own the process.
- Do not assume Lantern's environment variables can alter it.
- Report the specific configuration requirement.
- Do not terminate external Meilisearch processes automatically.

Support the same behavior on Windows and Linux.

## 4. Verify the URL Exactly

Inspect the configured `lantern-granite` REST embedder URL.

Check:

- HTTP or HTTPS scheme.
- Hostname or IP address.
- Port.
- Request path.
- IPv4 versus IPv6 resolution.
- Whether `localhost` resolves differently from `127.0.0.1`.
- Whether the configured address is reachable from the Meilisearch process.

Do not replace `localhost` with `127.0.0.1` blindly.

If the endpoint genuinely listens on IPv4 loopback, use a consistent explicit IPv4 address and configure the corresponding network allowance.

Also verify that URL construction does not produce malformed schemes, paths, or hosts.

## 5. Reproduce with a Controlled Hybrid Search

Before changing crawler behavior:

1. Confirm the LlamaPure endpoint responds to a direct embedding request.
2. Confirm that the Meilisearch process has the intended network policy.
3. Read the actual REST embedder settings from Meilisearch.
4. Submit a controlled vector search against `lantern-dev-chunks`.
5. Observe the LlamaPure server's incoming requests.
6. Confirm that Meilisearch receives valid embedding vectors.
7. Confirm the search no longer returns `Rejected URI`.

The verification must involve a request originating from Meilisearch.

A direct request from Lantern to LlamaPure is not sufficient.

## 6. Fix Readiness Reporting

Inspect the Embedding readiness state used by:

- `EmbedderSetupService`.
- `MeilisearchChunkIndexingService`.
- Hybrid Search.
- System Status.
- Crawler synchronization.

Do not mark the REST Embedder ready merely because:

- LlamaPure is running.
- The REST embedder settings exist.
- An index configuration task was submitted.
- A direct Lantern-to-LlamaPure inference request succeeded.

Report ready only after the required Meilisearch-originated operation succeeds.

When `Rejected URI` occurs, expose an actionable `EmbeddingNetworkPolicyRejected` diagnostic instead of a generic unavailable message.

Do not retry readiness validation in an uncontrolled tight loop.

## 7. Handle Repeated URI Rejection

Inspect the installed Meilisearch version and known behavior regarding retrying policy-rejected REST embedder URLs.

Reference:

https://github.com/meilisearch/meilisearch/issues/6223

Determine whether an available compatible Meilisearch release handles this deterministic failure without unnecessary retries.

Do not increase retry limits.

Do not try to solve a network policy rejection by increasing indexing task timeouts.

Do not suppress warnings as a substitute for successful connectivity.

## 8. Add Regression Tests

Add tests covering:

- Allowed IPv4 loopback endpoint.
- IPv6 loopback endpoint where supported.
- Rejected local endpoint.
- Invalid REST URL.
- Managed Meilisearch startup configuration.
- Externally managed Meilisearch.
- Environment variable not inherited by the running process.
- Actual Meilisearch-to-LlamaPure inference.
- Hybrid Search success.
- Readiness rejection and recovery.
- Windows and Linux startup behavior.

A real integration test must verify that the embedding server receives a request from Meilisearch.

## Acceptance Criteria

The fix is complete only when:

1. The actual embedding endpoint URL is confirmed.
2. The effective Meilisearch network policy is identified.
3. The cause of the URI rejection is verified.
4. The appropriate restricted allowance is applied to the actual Meilisearch process.
5. Meilisearch successfully invokes LlamaPure.
6. The response contains valid embedding vectors.
7. Hybrid Search no longer returns HTTP 400 `vector_embedding_error` for the controlled valid query.
8. The repeated `Rejected URI` messages disappear during normal operation.
9. Embedding readiness accurately reflects actual Meilisearch-to-LlamaPure connectivity.
10. Existing indexes, credentials, and documents are preserved.

## Required Completion Report

Report:

- Meilisearch version.
- Actual embedding endpoint.
- Meilisearch process owner and launcher.
- Supported network policy option.
- Effective process configuration.
- Root cause of the rejection.
- Files changed.
- Direct LlamaPure inference results.
- Meilisearch-originated inference results.
- Hybrid Search results.
- Tests executed and their actual outcomes.
- Remaining limitations.

**Do not declare success until an actual Meilisearch-originated embedding request reaches LlamaPure and Hybrid Search succeeds.**
