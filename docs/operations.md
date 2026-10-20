# Lantern Operations Guide

## Service Startup Dependencies

Lantern requires the following services before it is fully functional:

| Service | Required for | Startup behavior if unavailable |
|---------|-------------|--------------------------------|
| **SQLite** | All persistent operations | Fatal — Lantern will not start |
| **Meilisearch** | Search and indexing | Degraded — keyword search fails; admin UI accessible |
| **Embedding server** (LlamaPure) | Hybrid search | Degraded — keyword search continues; hybrid reports unavailable |

Start Meilisearch before starting Lantern. The embedding server is started and managed by Lantern itself if `Embedding:Enabled` is `true`.

### Recommended startup order

```
1. Meilisearch
2. Lantern (starts embedding server as a hosted service if enabled)
```

---

## Safe Restart Behavior

- Lantern persists all state to SQLite before acknowledging actions.
- Running crawler jobs that are interrupted by a controlled or uncontrolled shutdown are marked as interrupted on the next startup by `JobRecoveryService`.
- Pending jobs remain eligible and are resumed automatically.
- SQLite data persists correctly across restarts when `Storage:DataProtectionKeysPath` is configured.
- Authentication sessions survive restarts when Data Protection keys are persisted (see `Security.md`).

---

## Health Checks

There is no automated health-check endpoint in v1.0. Manual verification:

1. **Lantern**: Load `https://<host>/search` — the page must render.
2. **Meilisearch**: Load `http://127.0.0.1:7700/health` — should return `{"status":"available"}`.
3. **Embedding server**: Load `http://127.0.0.1:<port>/health` — should return HTTP 200 (when `Embedding:Enabled = true`).
4. **SQLite**: Run `sqlite3 lantern.db "PRAGMA integrity_check;"` and verify output is `ok`.
5. **Crawler jobs**: In the admin UI → Jobs, check for stuck `Running` jobs older than 24 hours.

---

## Resource Limits

### File crawler

| Limit | Configuration | Default |
|-------|--------------|---------|
| Maximum file size | `MaxFileSizeMB` per crawler | 100 MB |
| Crawl concurrency | `ConcurrencyLimit` in `FileCrawlerOptions` | 4 |
| Extraction per file | Enforced by `DocumentExtractor` | ~10 MB extracted text |

### Web crawler

| Limit | Configuration | Default |
|-------|--------------|---------|
| Maximum pages | `MaxPageCount` per crawler | 1000 |
| Maximum crawl depth | `MaxDepth` per crawler | 5 |
| Response size | `MaxResponseSizeBytes` | 10 MB |
| Request timeout | `RequestTimeoutSeconds` | 30 s |
| Redirects per request | `MaxRedirects` | 5 |
| Crawl concurrency | `ConcurrencyLimit` | 4 |
| Per-host rate limit | `RequestsPerSecond` | 1 req/s |

### Embedding

| Limit | Configuration | Default |
|-------|--------------|---------|
| Input token limit | `Embedding:MaxTokens` | 512 |
| Queue capacity | `Embedding:MaxQueueDepth` | 64 |
| Inference concurrency | Single worker | 1 |

### Search

| Limit | Value |
|-------|-------|
| Query max length | 500 characters |
| Results per page | 10 |

---

## Common Failure Modes

### Meilisearch unavailable

**Symptom**: Search returns "Search is temporarily unavailable." The admin index status page shows "Could not reach".

**Cause**: Meilisearch process is not running, or the configured URL is wrong.

**Recovery**:
1. Start Meilisearch.
2. Verify `Meilisearch:Url` in configuration matches the binding address.
3. Crawler jobs that failed during the outage can be retried from the Jobs admin page.

### Embedding server unavailable

**Symptom**: Hybrid search mode reports "Search is temporarily unavailable." Keyword mode continues to work.

**Cause**: The embedding server failed to start (model file missing, insufficient memory) or the model is not loaded.

**Recovery**:
1. Check Lantern application logs for embedding server startup errors.
2. Verify the model file path in `Embedding:ModelPath`.
3. Restart Lantern if the embedding worker is in a faulted state.

### Crawler jobs stuck as Running

**Symptom**: Jobs show `Running` in the admin UI but no crawl is in progress.

**Cause**: Lantern was terminated while a job was running and the recovery service did not run (e.g., the database was not accessible at startup).

**Recovery**:
1. Restart Lantern normally — `JobRecoveryService` marks interrupted jobs on startup.
2. If jobs remain stuck after restart, use the "Cancel" button in the admin UI and then retry.

### SQLite lock errors

**Symptom**: Operations fail with `database is locked` errors.

**Cause**: A long-running transaction, multiple concurrent writers, or a crashed process holding a lock.

**Recovery**:
1. Stop Lantern.
2. Check for any `lantern.db-wal` or `lantern.db-shm` files — these indicate an open WAL transaction.
3. Start Lantern again — EF Core will recover the WAL on reconnect.
4. If the database is corrupted, run `PRAGMA integrity_check;` and restore from backup if necessary.

### Login sessions invalidated after restart

**Symptom**: Administrators are logged out after Lantern restarts.

**Cause**: `Storage:DataProtectionKeysPath` is not configured or the keys directory is empty/deleted.

**Recovery**:
1. Ensure `Storage:DataProtectionKeysPath` points to a persistent, writable directory.
2. After configuring, restart Lantern — new keys are generated and future sessions will survive restarts.

---

## Troubleshooting

### Enable verbose logging

Add to `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Lantern": "Debug",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

### Check migration state

```powershell
sqlite3 .\data\lantern.db "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;"
```

Expected output includes all 6 migrations up through Phase 9.

### Verify Meilisearch index attributes

```powershell
Invoke-RestMethod -Uri "http://127.0.0.1:7700/indexes/lantern-docs/settings/filterable-attributes" `
    -Headers @{ "Authorization" = "Bearer $env:MEILI_MASTER_KEY" }
```

### Check search index document count

```powershell
Invoke-RestMethod -Uri "http://127.0.0.1:7700/indexes/lantern-docs/stats" `
    -Headers @{ "Authorization" = "Bearer $env:MEILI_MASTER_KEY" }
```

---

## Least-Privilege Process Identity

For production deployments:

- Run Lantern as a dedicated Windows service account (not SYSTEM or Administrator).
- Grant **read** access to all approved crawler source directories.
- Grant **read/write** access to the Lantern data directory (`./data/`), keys directory (`./keys/`), and log directory.
- Grant **read/write** access to the Meilisearch data directory if Meilisearch runs under the same account.
- The account does **not** require write access to crawler source directories — read-only is sufficient for indexing.

Windows service packaging and installation are planned for Phase 12.
