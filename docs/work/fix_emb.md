# Lantern — Investigate Parent Document Indexing Timeouts with Embedding Enabled

## Confirmed Symptoms

When embedding is enabled, crawling fails during parent document indexing.

The current exception is:

```text
System.TimeoutException:
Meilisearch upsert documents, task 78 did not finish
within 60 seconds. Its outcome is unknown.
```

The exception originates from:

- `MeilisearchIndexingService.WaitAsync()`
- `MeilisearchIndexingService.PopulateAsync()`
- `MeilisearchIndexingService.UpsertBatchAsync()`
- `FileSynchronizationService.SyncAsync()`

Previous failures occurred in `MeilisearchChunkIndexingService`, also while waiting for asynchronous tasks.

This suggests a broader indexing pipeline or Meilisearch task throughput problem, rather than a defect isolated to one method.

## Investigation Priorities

### 1. Determine the actual outcome of task 78

Query Meilisearch task 78 and nearby tasks.

Record:

- Task status and type.
- Index UID.
- Enqueue time.
- Processing start time.
- Completion time, if available.
- Task details and error.
- Queue delay.
- Processing duration.

Determine whether the task is queued, processing, completed, failed, or canceled.

Do not treat the 60-second client timeout as proof of server-side task failure.

### 2. Inspect embedding configuration on both indexes

Identify the actual parent and chunk index UIDs.

Retrieve embedder settings from both indexes.

Determine whether:

- The parent index has an embedder.
- The chunk index has an embedder.
- Both indexes are generating embeddings.
- Any embedder is configured but not required by the architecture.
- Index settings changes are causing unexpected reindexing.

If Hybrid Search operates on chunks, evaluate whether the parent index should remain keyword-only.

Do not delete parent-index embeddings or change settings until the impact on existing search behavior has been assessed.

### 3. Diagnose Meilisearch task throughput

Measure the queue and processing behavior with embedding disabled and enabled.

Use the same controlled document and equivalent starting conditions.

Compare:

- Parent indexing duration.
- Chunk indexing duration.
- Meilisearch task queue delay.
- Embedding requests.
- Embedding inference latency.
- Indexing failures.
- Concurrent crawler operations.

Determine whether the bottleneck is actual embedding inference, queued work, configuration changes, or excessive indexing concurrency.

### 4. Audit synchronous waiting

Inspect both:

- `MeilisearchIndexingService.WaitAsync()`
- `MeilisearchChunkIndexingService.WaitAsync()`

Avoid independent, hardcoded 60-second waits per file and per indexing operation.

Implement consistent task lifecycle management.

A timeout must preserve the task UID and unknown completion state.

Do not immediately resubmit tasks with unknown outcomes.

Implement durable reconciliation where necessary.

### 5. Verify document and chunk synchronization

Determine whether parent document upserts and chunk replacement are ordered correctly.

Ensure:

- Parent document indexing is tracked.
- Chunk indexing is tracked.
- Indexing state is not marked successful prematurely.
- Partial success is recoverable.
- Repeated crawling does not produce duplicate documents.
- Pending tasks cannot overwrite newer document versions unexpectedly.
- Existing searchable content is not deleted unnecessarily.

### 6. Review PPTX extraction diagnostics

The logs also contain:

```text
OpenMcdf.FileFormatException: Invalid header signature.
```

Investigate whether this is an expected first-chance exception from a format-detection fallback.

PPTX files are ZIP-based OOXML documents, not OLE Compound Files.

Ensure that normal PPTX extraction does not require successfully parsing the file with OpenMcdf.

If the exception is expected and correctly handled, avoid classifying it as the indexing failure.

If incorrect extractor dispatch occurs, correct format detection without breaking legacy PPT support.

### 7. Verify end-to-end search

After correcting indexing:

1. Crawl a controlled PPTX with embedding disabled.
2. Verify successful parent indexing.
3. Enable embedding.
4. Verify the intended embedder configuration.
5. Crawl a controlled document.
6. Verify parent and chunk indexing task outcomes.
7. Verify actual embedding inference.
8. Verify indexed vectors.
9. Execute Keyword Search.
10. Execute Hybrid Search.
11. Confirm source metadata and clipboard functionality remain correct.

## Acceptance Criteria

- Task 78's actual outcome is determined.
- Parent and chunk embedder configurations are understood.
- Unnecessary embedding work is identified and eliminated where appropriate.
- Meilisearch task backlogs are measured.
- Timeouts no longer masquerade as confirmed indexing failures.
- Pending tasks are reconciled safely.
- Crawl and indexing state remain consistent.
- Both indexing services use coherent task lifecycle handling.
- Keyword Search remains operational.
- Actual vector-backed Hybrid Search succeeds.
- Existing indexes and documents are preserved.

## Required Completion Report

Provide the actual task states, embedder configuration of each index, measured queue and inference durations, root cause, corrected implementation, and results of real end-to-end tests.

Do not report success based solely on longer timeouts or the absence of exceptions.

**The required outcome is a reliable indexing pipeline that completes or safely tracks both parent and chunk tasks, and produces searchable documents with valid embeddings.**
