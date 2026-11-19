# Lantern — Fix Keyword Search Returning Zero Results After Clipboard Implementation

## Confirmed Symptoms

Lantern's Keyword Search worked correctly before the search-result clipboard feature was implemented.

After the clipboard implementation:

1. The `/search` page initially experienced HTTP requests timing out after 100 seconds.
2. The timeout exceptions have now disappeared.
3. The `/search` page loads.
4. Searching for a previously successful keyword returns:

   `No documents matched your search.`

5. The Meilisearch index `lantern-dev-documents` exists.
6. The same keyword returned results before the clipboard changes.

The known reproduction URL is:

```text
https://localhost:7108/search?q=ライティング&page=1
```

## Objective

Identify and fix why Keyword Search now returns zero results.

**Treat this as a regression until evidence establishes otherwise.**

Do not assume that the absence of exceptions means search is working correctly.

Do not delete or recreate the existing Meilisearch index.

Do not clear SQLite or trigger a full reindex as a troubleshooting shortcut.

Preserve the file path, directory path, and URL clipboard functionality.

---

## Phase 1 — Establish the Actual Search Data State

### 1. Verify the Correct Meilisearch Instance

Identify the exact Meilisearch instance used by Lantern.

Record:

- Base URL and port.
- Active environment.
- Index UID.
- Data directory.
- Search API Key role.
- Meilisearch version.

Do not print authentication secrets.

Ensure that Lantern has not silently switched to another Meilisearch instance or index after recent configuration changes.

### 2. Verify Actual Indexed Documents

Inspect:

```text
lantern-dev-documents
```

Determine:

- Whether the index exists.
- Actual number of indexed documents.
- Whether previously searchable documents are still present.
- Whether their extracted text fields remain populated.
- Whether the relevant text fields are searchable.
- Whether unexpected index settings changes occurred.

**Index existence alone is not sufficient.**

If the index contains zero documents, investigate when and why its contents disappeared.

Do not recreate the index or reindex documents before establishing the cause.

### 3. Perform Direct Keyword Search

Execute a direct authenticated Meilisearch search using the exact query:

```text
ライティング
```

Use the configured parent document index.

First, execute a minimal search request without additional filters, attributes, hybrid options, or result transformations.

For example:

```http
POST /indexes/lantern-dev-documents/search
Content-Type: application/json

{
  "q": "ライティング",
  "limit": 20
}
```

Use the appropriate Search API Key through the existing secure development tooling.

Record:

- HTTP status.
- Meilisearch `estimatedTotalHits` or equivalent total.
- Number of returned hits.
- Returned document IDs.
- Names of returned document fields.
- Searchable attributes.
- Applied filters, if any.

Do not log sensitive document content.

### Required comparison

**Case A: Direct Meilisearch search returns results.**

The regression is likely in Lantern's request construction, result mapping, filtering, aggregation, or UI.

**Case B: Direct Meilisearch search returns zero results.**

Investigate index contents, searchable attributes, source data synchronization, and index configuration.

**Case C: Direct Meilisearch search fails.**

Investigate authentication, index access, and actual API errors separately.

Do not proceed with assumptions about which case applies.

---

## Phase 2 — Compare the Working and Failing Revisions

### 4. Identify the Relevant Git Changes

Locate:

- The last revision with working Keyword Search.
- The clipboard feature implementation.
- Subsequent changes intended to fix the HTTP timeout.

Inspect the complete diff between the working revision and the current version.

Focus on:

- Search page Razor components.
- Search result components.
- Search result DTOs.
- Parent document models.
- Search service interfaces.
- Meilisearch query construction.
- Search result deserialization.
- Result mapping.
- Source type detection.
- File path processing.
- Document identity handling.
- Search result aggregation.
- Pagination and filtering.
- Dependency injection.
- Credential configuration.

Do not revert unrelated features.

### 5. Test the Last Known Working Revision

If practical, run the previous revision against an isolated, compatible test configuration.

Use the same test query and equivalent index data.

Determine whether the regression is reproducible from code changes rather than differences in Meilisearch data or configuration.

If the original index has changed, document that limitation and use a controlled fixture for comparison.

---

## Phase 3 — Trace the Entire Keyword Search Pipeline

### 6. Add Search Result Count Diagnostics

Instrument the search pipeline.

Capture counts at each stage:

```text
HTTP /search
    |
    v
Query Parameter Binding
    |
    v
Keyword Search Request Construction
    |
    v
Meilisearch Response
    |
    v
Response Deserialization
    |
    v
Parent Document Mapping
    |
    v
Source Metadata Mapping
    |
    v
Post-Search Filtering
    |
    v
Pagination
    |
    v
Blazor Search Result Rendering
```

Record:

- Query value received by the component.
- Search mode.
- Selected index.
- Filter expression.
- Requested page and page size.
- Meilisearch total hits.
- Meilisearch returned hit count.
- Mapped result count.
- Filtered result count.
- Displayed result count.

Use structured logs.

Do not log API keys or private document contents.

### 7. Identify the First Stage Where Results Disappear

The critical question is:

**At which exact stage does a nonzero result count become zero?**

Examples:

```text
Meilisearch hits:        12
Deserialized hits:       12
Mapped results:           0
Displayed results:        0
```

This indicates a mapping problem.

Alternatively:

```text
Meilisearch hits:        12
Mapped results:          12
After filtering:          0
Displayed results:        0
```

This indicates a filtering problem.

Alternatively:

```text
Meilisearch hits:         0
Mapped results:           0
Displayed results:        0
```

This requires checking the actual Meilisearch request and index state.

Do not add fallback results to conceal the defect.

---

## Phase 4 — Audit Search Result Mapping Changes

### 8. Inspect Clipboard-Related DTO Changes

The clipboard feature may have added fields such as:

```text
SourceType
SourcePath
SourceUrl
DirectoryPath
OriginalPath
ParentDocumentId
```

Determine whether any new field became mandatory.

Look for logic equivalent to:

```csharp
if (string.IsNullOrWhiteSpace(document.SourcePath))
{
    continue;
}
```

or:

```csharp
results = results
    .Where(x => x.SourceType != null)
    .ToList();
```

Such logic may incorrectly discard existing indexed documents that predate the clipboard feature.

### Critical requirement

**Clipboard metadata must not be a prerequisite for a document to appear in search results.**

If a result lacks a source path:

- Preserve the search result.
- Disable only unavailable copy actions.
- Keep the title, snippet, and navigation behavior when valid.

Do not filter out valid search results merely because clipboard metadata is absent.

### 9. Maintain Backward Compatibility

Existing indexed documents may not contain newly introduced fields.

Ensure:

- Missing source fields are tolerated.
- Optional clipboard properties remain optional.
- Parent document IDs are mapped correctly.
- Search result deserialization remains compatible.
- Unknown source types do not cause result removal.
- Search hit text and scoring remain unaffected.

Do not require a full reindex solely for clipboard support.

If metadata migration is genuinely necessary, provide a safe, explicitly justified migration strategy.

---

## Phase 5 — Audit Query and Filtering Changes

### 10. Compare Generated Meilisearch Requests

Compare the exact search request before and after the clipboard changes.

Inspect:

- Query text.
- Index UID.
- Search mode.
- Filters.
- Sort rules.
- Distinct attributes.
- Searchable attributes.
- Attributes to retrieve.
- Limit.
- Offset.
- Pagination behavior.

The clipboard feature should not change search matching behavior.

### 11. Check Unexpected Source-Type Filters

Investigate whether the implementation introduced restrictions such as:

```text
sourceType = "file"
```

or:

```text
sourceType IN ["file", "web"]
```

These may exclude documents indexed with older source-type values or missing metadata.

Do not add source-type filtering solely to determine available clipboard actions.

Clipboard actions should be selected during result presentation.

### 12. Check Attributes to Retrieve

Determine whether the search request now restricts returned fields.

For example:

```json
{
  "attributesToRetrieve": [
    "id",
    "title",
    "sourcePath"
  ]
}
```

This could omit fields required by the existing result mapper.

Restore all fields required for normal search result construction.

Add clipboard fields as optional metadata.

### 13. Verify Pagination

The reproduction request uses:

```text
page=1
```

Check that:

- UI page numbering is interpreted correctly.
- Meilisearch offsets are calculated correctly.
- The first page does not accidentally skip all available results.
- Total hit counts are preserved.
- Empty pages are distinguished from zero matching documents.

If Meilisearch returns hits but Lantern requests an out-of-range offset, correct the pagination logic.

Do not display "No documents matched your search" when matching documents exist but the requested page is invalid.

---

## Phase 6 — Check Index Content and Settings

### 14. Investigate Data Loss Only If Verified

If direct Meilisearch search also returns zero results, inspect:

- Index document count.
- Indexed document content.
- Searchable attributes.
- Recent index creation operations.
- Index replacement operations.
- Document deletion tasks.
- Crawler synchronization.
- Failed indexing tasks.
- Environment configuration changes.

The earlier `index_already_exists` error may indicate that index initialization was recently modified.

Verify that the fix did not accidentally:

- Select a different index.
- Recreate or swap an index.
- Reset index settings.
- Remove searchable content.
- Submit document deletions.
- Mark indexed documents as deleted.

These are hypotheses to verify, not established causes.

### 15. Do Not Mask Data Loss

If documents have genuinely disappeared, determine the cause before restoring them.

Provide a safe recovery method using the existing crawler and indexing pipeline.

Preserve:

- SQLite metadata.
- Categories.
- Crawler configuration.
- Existing document IDs.
- Unrelated indexed documents.

Do not execute destructive recovery automatically.

---

## Phase 7 — Fix the Regression

### 16. Apply the Smallest Correct Fix

Based on evidence, correct the responsible stage.

Expected design:

- Search matching remains independent of clipboard functionality.
- Clipboard metadata is optional.
- Search results are not discarded because of missing paths.
- Copy actions are rendered only when a valid source location exists.
- Source-platform path handling does not modify search queries.
- Search response mapping remains backward compatible.
- Pagination and filtering preserve their previous behavior.

Do not merely modify the empty-results message.

Do not insert fake results.

Do not disable source authorization checks.

Do not increase HTTP timeouts.

---

## Phase 8 — Regression Testing

### 17. Keyword Search Tests

Verify:

1. Japanese keyword search.
2. English keyword search.
3. Matching documents are returned.
4. No-match queries return zero results correctly.
5. Pagination works.
6. Category filtering works.
7. Missing optional metadata does not remove search hits.
8. Existing indexed documents remain searchable.
9. Clipboard actions do not affect search matching.
10. Search works after repeated page navigation.

### 18. Legacy Document Compatibility Tests

Create test documents representing the old Meilisearch schema.

Include records without:

- `SourceType`.
- `SourcePath`.
- `DirectoryPath`.
- New clipboard-related properties.

Verify that:

- They remain searchable.
- Titles and snippets render.
- Appropriate copy actions are disabled.
- No exceptions occur.
- No silent filtering occurs.

### 19. End-to-End Tests

Use real authenticated Meilisearch.

Test sequence:

1. Index a known test document.
2. Search for a unique known phrase.
3. Verify a nonzero Meilisearch hit count.
4. Verify a nonzero Lantern result count.
5. Verify the search result renders.
6. Verify clipboard actions where metadata is available.
7. Index a legacy-format document without clipboard metadata.
8. Verify that it still appears in search.
9. Verify the missing copy action is handled gracefully.
10. Repeat with Japanese text.

### 20. Regression Verification

Run the exact reported query:

```text
/search?q=ライティング&page=1
```

Confirm that it returns the previously matching documents, provided those documents still exist and their relevant content remains indexed.

If the documents no longer exist in the index, report that separately and investigate the data loss.

---

## Acceptance Criteria

The task is complete when:

- The first stage causing search results to disappear is identified.
- The exact regression-causing code change is documented.
- Direct Meilisearch results and Lantern results agree for equivalent search parameters.
- Existing indexed documents remain searchable.
- Missing clipboard metadata does not remove valid search results.
- Japanese Keyword Search works.
- Pagination behaves correctly.
- Clipboard actions still work.
- No new HTTP timeout occurs.
- Existing Meilisearch data is preserved.
- Automated regression tests pass.

## Required Completion Report

Provide:

### A. Root Cause

Identify the exact method and code change responsible.

### B. Search Pipeline Counts

Report actual counts from:

- Direct Meilisearch search.
- Meilisearch response received by Lantern.
- Result mapping.
- Post-processing and filtering.
- Final UI display.

### C. Index State

Report whether the expected documents still exist and whether their content remains searchable.

### D. Changes Made

List files modified and describe the correction.

### E. Compatibility

Explain how older indexed documents without clipboard metadata are handled.

### F. Tests

Provide actual commands and test outcomes.

### G. Remaining Issues

List any unresolved problems or data recovery requirements.

---

## Final Instructions

Fix the Keyword Search regression introduced after clipboard support was added.

The previous implementation returned matching documents for the same Japanese query.

The current implementation returns zero results without exceptions.

Do not treat this as a successful search operation merely because no exception is thrown.

Start by verifying actual Meilisearch documents and executing the query directly against the configured index.

Then trace the number of results through every Lantern search processing stage.

Inspect clipboard-related DTO changes, source metadata filtering, retrieved attributes, and pagination.

Ensure the new clipboard feature never excludes otherwise valid search results.

Preserve existing indexed documents and maintain backward compatibility.

Implement the smallest evidence-based correction and add real Meilisearch regression tests.

**Success means that previously matching documents appear in Keyword Search again, while file path, parent directory path, and URL copying continue to work.**
