# Lantern — Fix Hybrid Search Execution and Invalid SearchResult Mapping

## Confirmed Findings

Keyword Search works correctly.

Hybrid Search fails with:

`ArgumentNullException: Value cannot be null. (Parameter 'source')`

The exception occurs when the Blazor search component executes:

```csharp
results.Hits.Select(...)
```

The inspected result is:

```text
Type: SearchResult<MeiliChunkDocument>
EstimatedTotalHits: 0
Hits: null
Query: null
Limit: 0
ProcessingTimeMs: 0
```

This indicates that the Hybrid Search result is not being populated as expected. It does not establish that Meilisearch returned a legitimate empty result.

## Required Investigation

1. Trace the Hybrid Search method from the UI to the actual Meilisearch `SearchAsync()` invocation.
2. Identify every assignment and return path for `SearchResult<MeiliChunkDocument>`.
3. Check for default result construction, unexecuted search branches, failed deserialization, and swallowed exceptions.
4. Determine whether the actual Meilisearch HTTP request is executed.
5. Capture the HTTP status, index UID, query payload, and returned hit count.
6. Compare the parent index used by Keyword Search with the chunk index used by Hybrid Search.
7. Verify that the chunk index exists and contains indexed documents.
8. Verify that the configured embedder exists on the index being searched.
9. Verify indexing and embedding task completion, and actual vector availability where required.
10. Inspect whether the search response is incorrectly converted or mapped to the `MeiliChunkDocument` type.

## Important Design Requirements

- Do not return a default `SearchResult<T>` after a failed search.
- Do not represent invalid responses as legitimate zero-hit results.
- Do not simply replace null `Hits` with an empty collection without establishing whether null is valid under the SDK contract.
- Preserve meaningful exceptions and stack traces.
- Ensure Hybrid Search queries the intended index with valid parameters.
- Ensure the queried index contains the documents or chunks expected by the application.
- If Hybrid Search returns chunks, aggregate them to parent document results correctly.
- Do not require administrator credentials for ordinary search execution.
- Do not silently fall back to Keyword Search.
- Preserve the functioning Keyword Search and clipboard features.

## Required Tests

Use real authenticated Meilisearch and the configured embedding backend.

Verify:

- Keyword Search returns expected documents.
- Hybrid Search executes the actual Meilisearch request.
- Hybrid Search returns populated results for known matching documents.
- Empty results are represented correctly.
- Invalid or missing responses produce an explicit diagnostic error.
- Parent and chunk index identities are correct.
- Hybrid results are correctly mapped to parent search items.
- Japanese query `パフォーマンス計測` works with populated test data.
- Missing embeddings and unavailable embedders produce actionable errors.

## Completion Report

Report:

1. The exact method returning the unpopulated `SearchResult<MeiliChunkDocument>`.
2. Whether Meilisearch was actually called.
3. The index UID used by Hybrid Search.
4. The actual document count in that index.
5. The HTTP response and result count.
6. Whether the embedder and document vectors are available.
7. The root cause and code correction.
8. Actual test commands and outcomes.

**Success requires actual Hybrid Search execution and correctly populated, mapped search results. Eliminating the `ArgumentNullException` alone is insufficient.**
