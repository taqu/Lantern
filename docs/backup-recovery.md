# Lantern Backup and Recovery

## Overview

Lantern's persistent state lives in two systems:

| System | Contents |
|--------|----------|
| **SQLite** | Crawler definitions, categories, job history, crawl state, application settings, administrator credentials, Data Protection keys |
| **Meilisearch** | Document and chunk indexes, full-text content, embedding vectors |

These systems do not share a transaction. A backup represents a point-in-time snapshot of each system independently. After restoration, a re-index pass can reconcile any differences.

## Data to Protect

```
Lantern Data
├── SQLite database          (default: ./data/lantern.db)
├── Meilisearch index data   (default: ./meilisearch/data.ms/)
├── Application configuration (appsettings.json / environment)
├── Data Protection keys     (default: ./keys/)
└── Embedding model file     (GGUF — reprovisioned separately)
```

The embedding GGUF model is large and typically reprovisioned from its original source rather than included in routine backups.

---

## Backup Procedure

### 1. Stop crawling (recommended)

To ensure a consistent snapshot, complete or cancel running crawler jobs before backing up. Use the administration UI → Jobs to check status.

### 2. Back up SQLite

Use the SQLite `.backup` command or `sqlite3` CLI to produce a safe online backup:

```powershell
sqlite3 .\data\lantern.db ".backup .\backup\lantern-$(Get-Date -Format 'yyyyMMdd-HHmmss').db"
```

This is safe to run while Lantern is running. Do not copy the `.db` file directly while it is open — the WAL journal may be inconsistent.

### 3. Back up Meilisearch

Use the Meilisearch [snapshot or dump](https://www.meilisearch.com/docs/reference/api/snapshots) API:

```powershell
# Trigger a snapshot (Meilisearch Community Edition)
Invoke-RestMethod -Method POST -Uri "http://127.0.0.1:7700/snapshots" `
    -Headers @{ "Authorization" = "Bearer $env:MEILI_MASTER_KEY" }
```

Meilisearch writes snapshots to the `snapshots/` subdirectory of its data path. Move or copy the snapshot file to your backup location.

Alternatively, stop Meilisearch and copy its entire data directory.

### 4. Back up configuration and keys

```powershell
Copy-Item -Path .\appsettings.json  -Destination .\backup\
Copy-Item -Path .\keys\             -Destination .\backup\keys\ -Recurse
```

### 5. Protect backup files

Backup files may contain indexed internal documents and the Meilisearch master key. Store them in a location accessible only to authorized administrators. Do not expose backups through public HTTP endpoints.

---

## Restore Procedure

### 1. Stop Lantern and Meilisearch

Ensure both services are stopped before restoring data.

### 2. Restore SQLite

```powershell
Copy-Item .\backup\lantern-20261021-120000.db .\data\lantern.db
```

Verify the database is readable:

```powershell
sqlite3 .\data\lantern.db "PRAGMA integrity_check;"
# Expected output: ok
```

### 3. Restore Meilisearch

Copy the snapshot file to Meilisearch's snapshots directory and start Meilisearch with `--import-snapshot` pointing to the file:

```powershell
meilisearch --import-snapshot .\backup\meilisearch-snapshot.snapshot `
            --db-path .\meilisearch\data.ms
```

Or restore by replacing the data directory:

```powershell
Remove-Item .\meilisearch\data.ms -Recurse -Force
Copy-Item .\backup\meilisearch-data\ .\meilisearch\data.ms -Recurse
```

### 4. Restore configuration and keys

```powershell
Copy-Item .\backup\appsettings.json .\
Copy-Item .\backup\keys\ .\keys\ -Recurse -Force
```

### 5. Start services and verify

Start Meilisearch, then start Lantern. Verify:

- [ ] Lantern starts without migration errors.
- [ ] The administration login works.
- [ ] The settings page loads.
- [ ] Crawler definitions are visible.
- [ ] Category assignments are present.
- [ ] A keyword search returns expected results.
- [ ] Hybrid search works if the embedding model is configured.

---

## Reconciling SQLite and Meilisearch

Because SQLite and Meilisearch backups are taken at different times, some documents may be in one but not the other after restoration:

- **In SQLite but not Meilisearch**: Trigger a full re-crawl for the affected crawlers. The incremental sync logic will re-index missing documents without duplication.
- **In Meilisearch but not SQLite**: These are orphaned index entries. They will appear in search but will be removed on the next complete crawl cycle.

A coordinated backup (both systems quiesced at the same moment) eliminates the difference.

---

## Interrupted Jobs

After restoration, jobs that were `Running` at backup time are treated as interrupted. On next startup, `JobRecoveryService` marks them as interrupted and makes them eligible for retry. No data loss occurs — the next crawl re-processes any files that were not completed.

Incomplete scans (crawls that did not finish) do **not** trigger deletion reconciliation. Deletion only occurs after a complete scan.

---

## Failed Indexing Recovery

If Meilisearch was unavailable during a crawl:

1. The crawler job is recorded as failed in SQLite.
2. Previously indexed content remains in Meilisearch untouched.
3. Re-submitting the job re-attempts indexing without re-extracting unchanged files (if the fingerprint is still current).

---

## Category Synchronization Recovery

Search categories in SQLite are the authoritative source. If a category update is interrupted before Meilisearch was updated:

1. The category assignment remains correct in SQLite.
2. Trigger a re-crawl for the affected crawlers to re-sync `categoryIds` in the Meilisearch index.
3. Full re-extraction is not required — only the metadata update.

---

## SQLite Integrity Check

To verify database integrity before or after backup:

```powershell
sqlite3 .\data\lantern.db "PRAGMA integrity_check; PRAGMA foreign_key_check;"
```

If integrity check reports errors, do not proceed with the damaged database. Use a known-good backup and contact the administrator.

Do not automatically delete or recreate a corrupted database — it may contain recoverable data.

---

## Consistency Limitation

Lantern does not implement atomic cross-system backup. The SQLite backup and the Meilisearch snapshot represent different moments in time. This is a documented v1.0 limitation. The reconciliation procedure above (re-crawl after restore) is the supported path to consistency.
