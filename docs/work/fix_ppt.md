# Lantern — Fix PPT Extraction, Duplicate Index Creation, and Missing Document Entries

## Objective

Investigate and fix multiple related failures occurring when Lantern crawls legacy Microsoft PowerPoint `.ppt` documents.

The reported Meilisearch error is:

```text
Failed;
{
  "message": "Index `lantern-dev-documents` already exists.",
  "code": "index_already_exists",
  "type": "invalid_request",
  "link": "https://docs.meilisearch.com/errors#index_already_exists"
}
```

Additional observed symptoms:

- The crawled PPT document does not appear to have a database entry.
- The expected document is not searchable.
- Text content extracted from the `.ppt` file appears incorrect or incomplete.
- The crawler reports failure even though the document index already exists.

These symptoms may have separate causes.

**Your task is to investigate the complete PPT extraction and indexing pipeline, identify each root cause, and implement reliable corrections without deleting existing data.**

Do not assume that fixing the `index_already_exists` error will automatically fix PPT extraction or document persistence.

---

# Phase 1 — Reproduce and Trace the Failure

## 1. Inspect the Existing Implementation

Before modifying code, inspect:

- Legacy PPT extractor.
- OpenMcdf integration.
- Any native PPT parser or extraction worker.
- Extractor registration and format detection.
- Text normalization.
- Document metadata generation.
- Document identity calculation.
- SQLite repositories.
- Crawler job execution.
- Crawler scheduler and dispatcher.
- Meilisearch process manager.
- Meilisearch index initialization.
- Parent document indexing.
- Chunk indexing, if implemented.
- Meilisearch task monitoring.
- Search result retrieval.
- Error reporting and job status persistence.
- Existing integration tests.

Identify the exact functions involved in processing a `.ppt` file.

Use the actual repository structure.

## 2. Reproduce the Reported Error

Reproduce the failure using an authorized test presentation.

Capture:

- Complete exception stack trace.
- Meilisearch API endpoint and operation.
- Meilisearch task UID, if applicable.
- Index UID.
- Crawler job ID.
- Source document identity.
- Extraction result status.
- Extracted text length.
- SQLite persistence result.
- Meilisearch indexing result.

Do not log proprietary document text or credentials unnecessarily.

### Required investigation

Determine:

1. Whether the failure occurs before PPT extraction.
2. Whether it occurs after PPT extraction.
3. Whether the parent document is persisted to SQLite.
4. Whether indexing is attempted.
5. Whether Meilisearch accepts the document update.
6. Whether the associated asynchronous task succeeds.
7. Whether the document is filtered out of search results.
8. Whether a subsequent cleanup operation removes the document.

Do not assume the first visible exception is the only defect.

---

# Phase 2 — Fix Duplicate Meilisearch Index Creation

## 3. Find All Index Creation Calls

Search for:

```csharp
CreateIndexAsync(...)
```

and equivalent Meilisearch index creation calls.

Inspect:

- Application startup.
- Search initialization.
- Crawler startup.
- Document indexing.
- Chunk indexing.
- Category synchronization.
- Index settings updates.
- Embedder configuration.
- Development initialization scripts.

Determine whether index creation is being performed repeatedly or concurrently.

### Expected architecture

Index initialization should be handled by a dedicated infrastructure or maintenance service.

The crawler should not blindly create the same index for each document.

## 4. Make Index Initialization Idempotent

Implement or correct an idempotent index initialization operation.

Conceptually:

```csharp
await EnsureIndexExistsAsync(
    indexUid,
    primaryKey,
    cancellationToken);
```

Expected behavior:

- Create the index if it does not exist.
- Reuse it if it already exists.
- Validate its primary key.
- Validate relevant index settings.
- Apply necessary settings changes safely.
- Return successfully when an existing index is compatible.
- Fail with a useful diagnostic if the existing index is incompatible.

### Important

Do not delete and recreate an existing index merely because it already exists.

Do not clear existing documents.

Do not silently overwrite an incompatible schema.

## 5. Handle Index Creation Races

A simple existence check followed by index creation is not sufficient under concurrent startup.

For example:

```text
Worker A: Index does not exist
Worker B: Index does not exist
Worker A: Create index
Worker B: Create index
Worker B: index_already_exists
```

Handle this race correctly.

### Requirements

- Coordinate initialization within the Lantern process.
- Tolerate concurrent initialization attempts.
- Correctly handle an already-existing compatible index.
- Preserve all unexpected Meilisearch errors.
- Verify final index state after a competing creation.
- Respect the asynchronous nature of index-creation tasks.

### Important

Do not use a blanket exception handler.

Only handle `index_already_exists` as a benign concurrency outcome after verifying that the expected index exists and has compatible properties.

Authentication errors, incompatible primary keys, and configuration failures must remain actionable.

## 6. Respect Meilisearch Asynchronous Tasks

Meilisearch index creation and settings operations may return asynchronous tasks.

Ensure that initialization correctly tracks task completion.

### Requirements

- Capture task UIDs.
- Wait for required initialization tasks.
- Inspect final task status.
- Handle failed tasks.
- Handle canceled tasks.
- Respect timeouts and cancellation.
- Avoid racing document insertion against incomplete initialization.

A submitted index-creation task does not guarantee successful completion.

Do not assume that receiving a task object means the index is ready.

## 7. Verify the Existing Index

Inspect the existing:

```text
lantern-dev-documents
```

Verify:

- Index existence.
- Primary key.
- Searchable attributes.
- Filterable attributes.
- Sortable attributes.
- Displayed attributes.
- Actual document count.
- Required settings.
- Any relevant pending or failed tasks.

If Chunk Indexing exists, also verify its corresponding index.

Do not automatically recreate either index.

### Development environment

Ensure the application connects to the intended development Meilisearch instance.

Verify:

- Host.
- Port.
- Data directory.
- Credentials.
- Index naming.
- Environment selection.

Avoid confusing development and production indexes.

---

# Phase 3 — Investigate Missing Database Entries

## 8. Identify the Authoritative Document Store

Determine what the user-visible database represents.

Lantern may maintain:

- SQLite metadata records.
- Crawler source records.
- Index synchronization state.
- Parent documents in Meilisearch.
- Chunk documents in Meilisearch.

These are distinct storage layers.

Identify which one is missing the PPT document.

Do not assume SQLite and Meilisearch contain identical records.

## 9. Trace the Document Persistence Pipeline

Trace the document through:

```text
PPT File Discovered
        |
        v
Document Identity Generated
        |
        v
PPT Content Extracted
        |
        v
Extraction Validated
        |
        v
SQLite Metadata Persisted
        |
        v
Meilisearch Document Submitted
        |
        v
Meilisearch Task Completed
        |
        v
Synchronization State Updated
```

Adapt this sequence to the actual architecture.

### Required investigation

Determine:

- When a document record is created.
- Whether extraction must succeed before persistence.
- Whether failures are recorded.
- Whether the document identity is stable.
- Whether the SQLite transaction commits.
- Whether the indexing task is submitted.
- Whether indexing succeeds.
- Whether failed indexing causes rollback.
- Whether a cleanup or reconciliation job removes the record.

Capture non-sensitive identifiers to correlate stages.

## 10. Fix Document State Management

Ensure document processing has a clear persisted state.

Reuse the existing state model when possible.

Conceptual states:

```text
Discovered
Extracting
Extracted
Indexing
Indexed
Failed
```

### Requirements

- Extraction failure must not be reported as successful indexing.
- Indexing failure must not erase useful diagnostic state.
- Failed documents must remain identifiable for retry.
- Successful indexing must be confirmed.
- Repeated crawling must not create duplicate records.
- Recoverable failures must support retry.
- Permanent extraction failures must be classified clearly.

Avoid introducing a large state-machine rewrite unless necessary.

### Data integrity

Do not delete existing SQLite records merely because Meilisearch indexing fails.

Use the established source-of-truth policy.

## 11. Verify Stable Document Identity

Inspect document ID generation for `.ppt`.

Verify that:

- IDs are deterministic.
- Path normalization is correct.
- Windows and Linux path behavior is intentional.
- File extensions do not cause collisions.
- Repeated crawling updates existing records.
- Different documents cannot accidentally share one ID.

Check whether an existing document is being overwritten or incorrectly classified as unchanged.

Do not change the document ID scheme without a migration strategy.

---

# Phase 4 — Fix Legacy PPT Text Extraction

## 12. Reproduce Extraction Independently

Test the PPT extractor without involving:

- SQLite.
- Meilisearch.
- Crawler scheduling.
- Embedding.
- Hybrid search.

This is essential.

An extraction test must be able to open a valid `.ppt` file and return meaningful text directly.

### Capture

- Extractor implementation.
- Parsing library version.
- File signature.
- Extraction status.
- Extracted text length.
- Slide count.
- Slide ordering.
- Text content accuracy.
- Source metadata.
- Exception stack traces.

Do not use successful indexing as proof of successful text extraction.

## 13. Audit OpenMcdf Usage

If the implementation uses OpenMcdf, verify its responsibilities.

OpenMcdf reads OLE Compound File Binary Format structures.

It does not automatically implement complete PowerPoint binary text extraction.

Inspect access to streams such as:

```text
PowerPoint Document
Current User
Pictures
```

Determine which streams are required by the actual parser.

### Requirements

- Correctly open the compound file.
- Locate required streams.
- Validate stream lengths.
- Validate record offsets.
- Handle missing streams.
- Dispose resources correctly.
- Preserve parser exceptions.

Do not assume that reading compound streams as UTF-16 text is a valid PPT parser.

## 14. Validate PowerPoint Binary Parsing

Legacy `.ppt` files use PowerPoint-specific record structures.

Investigate whether the parser correctly handles:

- Record headers.
- Nested record containers.
- Record lengths.
- Persist directories.
- Slide references.
- Text records.
- Unicode text.
- Text encodings.
- Slide ordering.
- Speaker notes.

### Critical requirement

Do not implement extraction using arbitrary printable-string scanning as the primary parsing method.

Such an approach may return:

- Incomplete text.
- Duplicate text.
- Incorrect slide order.
- Encoding corruption.
- Unrelated binary fragments.

Use a format-aware parser.

## 15. Evaluate the Existing Parsing Library

If the current parser cannot correctly process valid PPT documents, evaluate suitable alternatives.

Possible approaches:

- Correct the current specification-aware parser.
- Adopt a maintained, compatible .NET parser.
- Use a suitable native C/C++ parser through a controlled interface.
- Use a dedicated extraction worker for unsafe native parsing.

### Requirements

- Windows x64 support.
- Linux x64 support.
- Japanese text extraction.
- Multiple slide support.
- Offline operation.
- No mandatory Microsoft Office.
- No Java runtime.
- No mandatory LibreOffice installation.
- Acceptable redistribution license.
- Reasonable maintenance and security posture.

Do not assume C++ alone guarantees correctness.

Do not introduce a native dependency without verifying actual binary-format coverage.

## 16. Required PPT Extraction Behavior

Valid PowerPoint presentations must produce:

- Meaningful slide text.
- Slide ordering.
- Slide numbers.
- Multiple text boxes.
- Japanese and English characters.
- Speaker notes where supported.
- Useful presentation metadata.

### Example normalized output

```text
Document:
  presentation.ppt

Slide:
  1

Text:
  Introduction to Rendering

Slide:
  2

Text:
  Shader Pipeline Architecture
```

Use Lantern's actual extraction model.

Do not introduce a separate result format unnecessarily.

## 17. Detect Empty or Invalid Extraction

A valid presentation may contain no extractable text, but parser failure must not be silently converted into an empty document.

Distinguish:

- Valid document with text.
- Valid document without extractable text.
- Unsupported PPT version.
- Encrypted presentation.
- Malformed presentation.
- Parser failure.
- Resource limit exceeded.

### Requirements

- Empty output must be intentional and classified.
- Failed parsing must produce a meaningful error.
- Do not index binary garbage.
- Do not substitute filenames for missing extracted content while reporting full success.
- Do not mark extraction successful solely because no exception occurred.

## 18. Preserve Japanese Text

Test Japanese presentations explicitly.

Verify:

- Unicode text records.
- Correct character decoding.
- No replacement-character corruption.
- No excessive missing characters.
- Correct text ordering.
- Consistent newline handling.

Do not apply generic UTF-8 decoding directly to PPT binary streams.

---

# Phase 5 — Integrate Extraction with Indexing

## 19. Validate Extraction Before Indexing

After PPT extraction, validate the normalized result.

Check:

- Extraction status.
- Content length.
- Structural consistency.
- Slide metadata.
- Resource limits.

Only valid extraction results should proceed through the normal indexing workflow.

If extraction fails, preserve the failure state for diagnosis and retry.

## 20. Verify Parent Document Indexing

Inspect the payload sent to:

```text
lantern-dev-documents
```

Verify:

- Stable document ID.
- Source path.
- Filename.
- Title.
- Extracted text.
- File type.
- Categories.
- Relevant timestamps.
- Document metadata.

Confirm that the actual extracted PPT content reaches the Meilisearch payload.

### Important

Do not assume that the extracted text is indexed merely because the document metadata exists.

Check the searchable attributes.

If the body field is excluded from searchable attributes, the document may exist but remain unsearchable by its text.

## 21. Verify Indexing Task Completion

After submitting the parent document:

1. Capture the task UID.
2. Wait for successful completion or persist pending state.
3. Inspect failed task details.
4. Confirm the document can be retrieved by ID.
5. Confirm a known unique text phrase is searchable.
6. Update synchronization state only after the required operations succeed.

Do not claim successful indexing based solely on an HTTP response indicating task acceptance.

## 22. Verify Chunk Indexing

If Chunk Indexing is implemented, verify:

- PPT text is divided according to slide boundaries.
- Chunk IDs are stable.
- Parent relationships are correct.
- Slide metadata is preserved.
- Chunk documents are indexed.
- Embeddings are generated where configured.
- Failed embedding tasks are reported.
- Deleted PPT files remove obsolete chunks.

If Chunk Indexing is not implemented, report it separately.

Do not expand this bug fix into an unrelated complete Hybrid Search implementation.

## 23. Handle Partial Failures

Handle cases where:

- SQLite succeeds but Meilisearch fails.
- Meilisearch succeeds but SQLite status update fails.
- Index initialization succeeds but document insertion fails.
- Parent indexing succeeds but chunk indexing fails.
- Extraction succeeds but indexing is rejected.
- Application shuts down during indexing.

### Requirements

- Maintain recoverable state.
- Avoid lost documents.
- Avoid duplicate documents.
- Avoid deleting valid indexed data.
- Support idempotent retry.
- Preserve the original failure reason.

Do not assume a distributed transaction exists between SQLite and Meilisearch.

---

# Phase 6 — Error Reporting and Diagnostics

## 24. Separate Error Categories

Do not report all failures as a generic crawler error.

Use meaningful classifications such as:

```text
IndexInitializationFailed
IndexConfigurationMismatch
DocumentExtractionFailed
DocumentPersistenceFailed
DocumentIndexingFailed
MeilisearchTaskFailed
AuthenticationFailed
```

Adapt to the existing error model.

### Example

Instead of:

```text
Failed: Index already exists
```

The application should:

- Reuse an existing compatible index.
- Report an actual incompatibility if present.
- Continue document processing when index initialization succeeds.

For extraction failures, report the extractor error independently.

## 25. Improve Diagnostic Correlation

Use structured logs containing:

- Crawler job ID.
- Source document ID.
- File extension.
- Extraction stage.
- Index UID.
- Meilisearch task UID.
- Operation status.

Avoid logging:

- API keys.
- Master keys.
- Passwords.
- Sensitive document content.
- Full binary document data.

Preserve complete exception stack traces in appropriate protected diagnostics.

---

# Phase 7 — Automated Tests

## 26. Index Initialization Tests

Use real authenticated Meilisearch.

Test:

1. Index does not exist.
2. Index creation succeeds.
3. Index already exists.
4. Existing compatible index is reused.
5. Existing incompatible primary key.
6. Concurrent initialization calls.
7. Repeated application startup.
8. Multiple crawler jobs.
9. Index settings reconciliation.
10. Authentication failure.
11. Failed asynchronous creation task.

### Critical acceptance test

Running a crawler against an existing `lantern-dev-documents` index must not fail with `index_already_exists`.

## 27. PPT Extraction Tests

Create deterministic test fixtures.

Include:

- Simple PPT with English text.
- PPT with Japanese text.
- Multiple slides.
- Multiple text boxes.
- Slides with notes.
- Empty slide.
- Valid document with no text.
- Corrupt compound file.
- Invalid PPT record.
- Encrypted presentation.
- Incorrect extension.

Tests must assert actual extracted text and slide metadata.

Do not merely assert that the extractor returns without throwing.

## 28. SQLite Persistence Tests

Use real SQLite.

Verify:

- Document record creation.
- Persistent identity.
- Extraction failure state.
- Indexing failure state.
- Successful indexing state.
- Repeated crawl idempotency.
- Recovery after partial failure.
- Preservation of unrelated crawler data.

Do not use only mocked repositories.

## 29. End-to-End Integration Tests

Use real SQLite and Meilisearch.

Test this sequence:

1. Initialize an empty test installation.
2. Create the parent index.
3. Crawl a valid PPT.
4. Extract known text.
5. Persist document metadata.
6. Index the document.
7. Confirm successful Meilisearch task completion.
8. Retrieve the document by ID.
9. Search for a known phrase.
10. Repeat the crawl.
11. Confirm no duplicate record.
12. Confirm no duplicate index creation failure.
13. Modify the PPT.
14. Recrawl.
15. Confirm updated search content.
16. Restart Lantern.
17. Recrawl again.
18. Confirm stable behavior.

Also test against a pre-existing compatible index containing unrelated documents.

Do not clear those documents.

## 30. Windows and Linux Verification

Run relevant tests on both platforms.

### Windows

- Legacy PPT extraction.
- Japanese text.
- SQLite persistence.
- Meilisearch indexing.
- Repeated crawling.
- Existing index behavior.

### Linux

Run the same tests on the available Linux environment.

Verify platform-specific parser dependencies if any.

Do not claim cross-platform success based only on compilation.

---

# Phase 8 — Data Recovery

## 31. Detect Previously Failed PPT Documents

Investigate whether the earlier failed crawls left:

- SQLite records.
- Failed job records.
- Partial Meilisearch documents.
- Incorrect fingerprints.
- Stale synchronization states.

Determine whether the crawler will retry these documents automatically.

### Important

A previously failed PPT must not be permanently skipped merely because its file modification time or fingerprint has not changed.

## 32. Implement Safe Reprocessing

If necessary, provide a targeted retry or repair operation.

Requirements:

- Reprocess failed PPT documents.
- Preserve unrelated documents.
- Avoid duplicate IDs.
- Avoid rebuilding all indexes unnecessarily.
- Preserve categories and crawler configuration.
- Support cancellation.
- Report actual recovery outcomes.

Do not delete the development SQLite database or recreate Meilisearch indexes to recover failed documents.

---

# Phase 9 — Documentation

## 33. Update Developer Documentation

Explain:

- Index initialization ownership.
- Idempotent index creation.
- Meilisearch asynchronous task handling.
- Document persistence states.
- PPT extraction architecture.
- Legacy PPT limitations.
- How to diagnose extraction failures.
- How to retry failed documents.

## 34. Update English and Japanese Documentation

Update relevant documentation in:

```text
docs/en/
docs/ja/
```

Follow the existing repository organization.

Ensure the documentation accurately describes supported `.ppt` extraction.

Do not advertise reliable PPT support until the corresponding tests pass.

---

# Acceptance Criteria

The issue is resolved only when:

1. Existing Meilisearch indexes are reused safely.
2. Repeated index initialization is idempotent.
3. Concurrent initialization does not cause crawler failures.
4. Existing documents and index settings are preserved.
5. Valid `.ppt` files produce meaningful extracted text.
6. Japanese PPT text is decoded correctly.
7. Slide boundaries and source locations are preserved.
8. Invalid or encrypted PPT files produce classified errors.
9. Successful PPT documents are persisted according to Lantern's database architecture.
10. Extracted text reaches the parent document index.
11. Meilisearch document indexing tasks complete successfully.
12. The indexed PPT can be retrieved and searched.
13. Repeated crawling does not duplicate documents.
14. Failed PPT documents can be retried.
15. Partial failures remain recoverable.
16. Windows and Linux tests pass.
17. Existing PDF, DOCX, PPTX, and other extractors are unaffected.
18. Documentation reflects the actual implementation.

**Do not consider this fixed merely because the `index_already_exists` exception disappears.**

The PPT document must also be correctly extracted, persisted, indexed, and searchable.

---

# Required Completion Report

## A. Root Cause of `index_already_exists`

Identify the exact function that creates the existing index again.

Explain whether the cause was:

- Repeated initialization.
- Concurrent initialization.
- Incorrect index existence detection.
- Asynchronous task handling.
- Incorrect environment configuration.

## B. Root Cause of Missing Database Entry

Identify which store lacked the document:

- SQLite.
- Meilisearch parent index.
- Meilisearch chunk index.

Explain where processing stopped and whether any record was removed.

## C. Root Cause of Incorrect PPT Text

Identify the parser defect and describe how it was corrected.

Explain whether OpenMcdf or another parsing library was involved.

## D. Architecture Changes

Describe the final index initialization and document synchronization flow.

## E. Files Changed

List all important modified source files, tests, and documentation.

## F. Test Results

Report actual test commands and outcomes.

Distinguish passed, failed, skipped, and unexecuted tests.

## G. Real-World Verification

If the originally failing PPT is accessible in the development environment, rerun its extraction and indexing.

Report:

- Extraction status.
- Extracted text length.
- Slide count.
- SQLite persistence status.
- Meilisearch indexing task status.
- Search verification result.

Do not disclose private document contents.

## H. Remaining Limitations

Document unresolved PPT format variants, unsupported features, and outstanding indexing issues.

---

# Final Instructions

Investigate and fix Lantern's legacy PPT processing failure.

Treat the duplicate Meilisearch index creation, missing document persistence, and incorrect text extraction as separate potential defects.

Make index initialization idempotent and concurrency-safe.

Do not recreate or delete an existing compatible index.

Respect Meilisearch asynchronous task completion and error reporting.

Trace document persistence across SQLite and Meilisearch.

Correct legacy PPT text extraction using a format-aware parser.

Verify Japanese text, slide ordering, and source metadata.

Ensure failed documents remain recoverable and can be retried.

Add deterministic unit and end-to-end integration tests using real SQLite and authenticated Meilisearch.

Verify on Windows and Linux.

Preserve existing documents, categories, indexes, and crawler configuration.

**The primary success condition is that a valid PPT document can be repeatedly crawled without index-creation conflicts, its text is accurately extracted, its metadata is persisted, and its content becomes searchable through Lantern.**
