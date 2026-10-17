# Lantern — Phase 1: Search UI Prototype

## Role

You are a senior .NET engineer implementing Phase 1 of Lantern, a lightweight, self-hosted full-text search platform for Windows and intranet environments.

Your task is to implement the first working search interface using Blazor and Meilisearch.

**This is an implementation task, not a planning task.**

Inspect the existing repository, reuse the Phase 0 foundation, implement working functionality, execute tests, and document the results.

Do not implement features assigned to later phases.

## 1. Project Context

Lantern aims to provide a simpler, lightweight alternative to Fess.

The planned application provides:

- Full-text search powered by Meilisearch.
- A browser-based search interface.
- A browser-based administration interface.
- Local filesystem and network-share crawling.
- Web crawling.
- Document text extraction using Toxy and appropriate fallback libraries.
- Crawler scheduling and execution management.
- Native Windows deployment without Docker.

### Technology stack

- C# / .NET 10 LTS
- ASP.NET Core
- Blazor Web App with Interactive Server rendering
- Meilisearch Community Edition
- Official Meilisearch .NET client where appropriate
- xUnit for testing

SQLite, document extraction, crawler management, and background jobs are planned for later phases.

## 2. Phase 1 Objective

Implement a minimal but functional full-text search interface that queries a real Meilisearch instance.

Users should be able to:

1. Open Lantern in a browser.
2. Enter a search query.
3. Submit the query.
4. View matching documents.
5. Read highlighted text snippets.
6. Navigate between result pages.
7. Open the original document URL when a safe, supported link is available.
8. Receive understandable messages for empty results or errors.

Use an existing Meilisearch index populated by a development seed utility.

Do not implement automatic crawling or production document indexing.

The implementation should establish a clean starting point for Phase 2.

## 3. Inspect the Existing Repository

Before changing code:

1. Examine the solution structure created in Phase 0.
2. Read `README.md`, `docs/architecture.md`, and the Phase 0 feasibility report.
3. Identify existing Meilisearch integration code.
4. Identify existing dependency injection and configuration patterns.
5. Check the installed Meilisearch client version.
6. Run the existing build and tests.
7. Identify reusable classes and utilities.

Preserve existing functionality.

Do not recreate or replace working Phase 0 components unnecessarily.

If the actual repository differs from the initial roadmap, adapt to its existing conventions rather than forcing a restructuring.

## 4. Scope

### In scope

- Search page.
- Search text field.
- Search submission.
- Server-side Meilisearch integration.
- Search result rendering.
- Basic snippets and highlighting.
- Result pagination.
- Loading indicator.
- Empty and no-results states.
- Connection and query error handling.
- Configurable Meilisearch connection.
- Development sample index.
- Search-related automated tests.
- Minimal documentation updates.

### Out of scope

Do not implement:

- Local file crawling.
- Web crawling.
- Document extraction.
- Production indexing pipeline.
- SQLite persistence.
- Administration UI.
- Authentication and user management.
- Advanced search filters.
- Sorting configuration.
- Search analytics.
- Autocomplete or search suggestions.
- Semantic search or AI features.
- Search result previews.
- Windows service integration.
- Automatic Meilisearch process management.
- Distributed or background job infrastructure.

Avoid speculative abstractions for these future features.

## 5. Search Interface Design

Implement a conventional, readable search interface.

The visual style should resemble a traditional search engine rather than a dashboard.

Prioritize usability, clarity, and predictable behavior.

### Search page layout

Use a layout similar to:

```text
----------------------------------------------------
 Lantern
----------------------------------------------------

 [ Search documents...                    ] [Search]


 About 128 results

 Document Title
 https://example.local/documents/example.pdf
 ...matching words appear in the document text...
 PDF

 --------------------------------------------------

 Another Document
 C:\Documents\example.txt
 ...another matching passage from the text...
 TXT

 --------------------------------------------------

                  Previous  1  2  3  Next

----------------------------------------------------
```

This is a conceptual layout, not a strict pixel specification.

### Visual requirements

- Simple typography.
- Neutral background.
- Clear text hierarchy.
- Readable spacing.
- Conventional links and buttons.
- Minimal decorations.
- No animated transitions.
- No cards around every result unless clearly useful.
- No unnecessary icons or visual effects.
- No third-party UI framework.

Use ordinary CSS and built-in Blazor components.

Do not introduce Tailwind, Bootstrap replacements, JavaScript frameworks, or component libraries unless the repository already depends on them and reuse is justified.

The interface should work comfortably in a desktop browser while remaining usable on narrower screens.

## 6. Routing and Navigation

Implement a search route:

`/search`

The application home page may redirect to `/search` or display the search interface directly.

Use URL query parameters for shareable search state.

Preferred URL format:

`/search?q=lantern&page=1`

Requirements:

- `q` represents the search query.
- `page` represents the current page, starting at 1.
- Missing or invalid page numbers fall back to page 1.
- Submitting a new query resets pagination to page 1.
- Reloading a search URL restores the query and page.
- Browser Back and Forward navigation should restore search state.
- Query parameters must be URL-encoded correctly.

Prefer URL-driven state rather than maintaining multiple conflicting representations of the same search state.

Do not use JavaScript for navigation when Blazor can handle it directly.

## 7. Search Service

Implement a small server-side search service.

Suggested interface:

```csharp
public interface ISearchService
{
    Task<SearchResponse> SearchAsync(
        string query,
        int page,
        CancellationToken cancellationToken = default);
}
```

The exact interface may be adjusted to existing project conventions.

### Responsibilities

The search service should:

1. Validate input.
2. Normalize page numbers.
3. Execute the Meilisearch query.
4. Request useful formatted snippets.
5. Convert Meilisearch responses into simple application models.
6. Return result counts and pagination information.
7. Handle expected Meilisearch errors.
8. Honor cancellation where supported.
9. Keep Meilisearch-specific types out of Blazor UI components.

Avoid implementing a generic search-provider framework.

Lantern uses Meilisearch as its search backend.

Do not build abstractions for Elasticsearch, Solr, SQL full-text search, or other engines.

### Suggested response models

```csharp
public sealed record SearchDocument(
    string Id,
    string Title,
    string? Url,
    string? Path,
    string? Snippet,
    string? DocumentType);

public sealed record SearchResponse(
    IReadOnlyList<SearchDocument> Documents,
    long TotalHits,
    int Page,
    int PageSize);
```

These models are illustrative.

Use suitable names and types consistent with the repository.

Do not add fields that are not needed by the prototype.

## 8. Meilisearch Integration

Reuse the Meilisearch client and connectivity code validated during Phase 0 whenever practical.

### Configuration

Support configuration similar to:

```json
{
  "Meilisearch": {
    "Url": "http://127.0.0.1:7700",
    "IndexName": "lantern-documents"
  }
}
```

Supply credentials separately through environment variables, user secrets, or other appropriate server-side configuration.

Do not commit real credentials.

Do not send Meilisearch API keys to browser components.

Meilisearch should normally be accessed from the ASP.NET Core server.

### Query behavior

Implement:

- Full-text queries.
- A fixed page size of 10 results.
- Offset-based pagination.
- Result count where available.
- Formatted text snippets.
- Highlighted query matches.

Use Meilisearch's native formatted response capabilities where suitable.

Do not build custom query-tokenization or highlighting algorithms.

Use a stable application-controlled mapping from indexed documents to UI models.

Do not render Meilisearch-generated markup directly as trusted HTML.

If highlighting markup is rendered, explicitly restrict it to safe generated markup and ensure all source text remains HTML-encoded.

Prefer a safe rendering strategy with minimal complexity.

### Result count semantics

Meilisearch may provide exact or estimated hit counts depending on query options and engine behavior.

Display counts accurately according to the selected API response.

Do not label an estimated count as exact.

Do not invent a count if it is unavailable.

### Failure handling

Handle these cases:

- Meilisearch is unavailable.
- API authentication fails.
- The configured index does not exist.
- The query is invalid.
- The query times out or is canceled.
- Meilisearch returns an unexpected response.

Show a brief and understandable error message in the UI.

Log relevant technical details on the server.

Never display credentials, stack traces, or internal connection secrets to users.

## 9. Development Sample Index

Provide a repeatable way to populate a development-only Meilisearch index.

Reuse the Phase 0 feasibility utility if appropriate.

Otherwise create a small standalone seeding command.

The seed dataset should contain at least 20 documents with varied content.

Include examples of:

- PDF documents.
- PowerPoint presentations.
- Word documents.
- Plain-text files.
- Website pages.
- English content.
- Japanese content.
- Documents with overlapping keywords.
- Queries returning no results.

These are search records only.

Actual file parsing and crawling are not required.

### Sample record

```json
{
  "id": "sample-001",
  "title": "Lantern Architecture",
  "url": "https://example.com/architecture",
  "path": null,
  "content": "Lantern uses Meilisearch for fast full-text search.",
  "documentType": "HTML"
}
```

The index schema should support the search prototype without prematurely finalizing the production document schema.

### Seeding requirements

- Use a dedicated development index or explicit development-only mode.
- Do not overwrite arbitrary existing user indexes.
- Repeated execution should be idempotent.
- Wait for asynchronous indexing tasks to finish.
- Print useful completion information.
- Exit with a nonzero status when seeding fails.
- Do not depend on Docker.
- Avoid hardcoded production credentials.

Document the seeding command in the README.

## 10. Search Form Behavior

The search form should behave predictably.

### Requirements

- Users can submit by pressing Enter.
- Users can submit by clicking Search.
- The current query remains visible after submission.
- Leading and trailing whitespace is handled consistently.
- Empty queries are not sent as unrestricted Meilisearch searches.
- A maximum query length is enforced.
- Query text is treated as data, never HTML or executable code.
- Double-clicking or repeated submission must not corrupt UI state.

Choose a reasonable query-length limit and document it.

Do not implement live search on every keystroke.

A traditional explicit search submission is sufficient.

## 11. Search Results

Each result should display:

1. Document title.
2. Source URL or filesystem path.
3. A short text snippet.
4. Document type, if present.

Use the document title as the primary visual element.

### Source navigation

For safe supported web URLs, allow users to open the original document.

Validate allowed URL schemes.

Do not render `javascript:`, `data:`, or other unsafe schemes as navigable links.

Local filesystem paths require special care.

Do not assume that browsers can open arbitrary `file://` URLs from an HTTP or HTTPS page.

For Phase 1, display local filesystem paths as plain text unless there is an explicitly supported and safe navigation mechanism already available.

Do not implement a file-serving endpoint or local file download proxy in this phase.

### Missing fields

Handle documents that lack:

- Title.
- Snippet.
- URL.
- Path.
- Document type.

Use sensible fallbacks.

Never crash merely because an optional field is absent.

## 12. Pagination

Implement straightforward pagination.

### Requirements

- Fixed page size: 10.
- Page numbers start at 1.
- Display Previous and Next navigation when applicable.
- Preserve the active search query.
- Disable navigation beyond the valid result range.
- Handle invalid and extremely large page numbers.
- Avoid integer overflow when calculating offsets.
- Respect Meilisearch pagination limits.

Do not fetch all search results and paginate them in memory.

Pagination must be performed by the search backend.

If an out-of-range page is requested, handle it gracefully.

Do not implement infinite scrolling.

## 13. Loading, Empty, and Error States

Implement the following states:

### Initial state

Show the search field with a short instruction.

Do not execute a full-index query automatically.

### Loading state

Indicate that a search request is in progress.

Avoid elaborate animation.

### No results

Display a clear message such as:

"No documents matched your search."

### Search failure

Display an understandable error such as:

"Search is temporarily unavailable."

### Missing index

Show an actionable development message without exposing secrets.

Do not silently populate the index during an ordinary search request.

### Cancellation and stale results

Ensure that navigation or newer searches cannot cause an older search response to overwrite the currently requested results.

Use normal Blazor lifecycle and cancellation patterns as appropriate.

## 14. Accessibility and Basic Usability

Use semantic HTML and accessible controls.

Requirements:

- The search field has an accessible label.
- Buttons have understandable text.
- Keyboard submission works.
- Focus indication is visible.
- Search results use appropriate semantic structure.
- Error messages are readable and accessible.
- Pagination is keyboard accessible.
- Highlighted terms remain readable.
- Layout works without hover-dependent behavior.

Avoid introducing a custom accessibility framework.

Use standard browser and Blazor capabilities.

## 15. Testing

Add meaningful automated tests.

### Unit tests

Test:

- Empty query handling.
- Query whitespace normalization.
- Invalid page numbers.
- Offset calculation.
- Search response mapping.
- Missing optional metadata.
- Safe versus unsafe result URLs.
- Error mapping.
- Any custom snippet-sanitization logic.

### Integration tests

Using a real Meilisearch instance, verify:

1. A test index can be created.
2. Sample documents can be indexed.
3. A known query returns expected results.
4. A no-match query returns no documents.
5. Result pagination works.
6. Snippet formatting behaves as expected.
7. Integration test data can be cleaned up.

Use uniquely named test indexes to avoid damaging development data.

Integration tests must not require Docker.

If Meilisearch is unavailable, report that the integration tests were skipped or blocked. Do not report them as passed.

### UI tests

Where practical, use Blazor component tests to verify:

- Search form rendering.
- Search submission.
- Result list rendering.
- Empty-result message.
- Error message.
- Pagination controls.

Do not introduce a complete browser automation framework solely for Phase 1 unless the project already uses one.

## 16. Performance and Simplicity

The search interface should remain lightweight.

Guidelines:

- Avoid unnecessary client-side JavaScript.
- Avoid loading all search documents into memory.
- Avoid blocking synchronous HTTP requests.
- Use async/await for search operations.
- Keep returned snippets reasonably short.
- Limit query results per page.
- Avoid expensive per-result service calls.
- Keep UI component state small.
- Do not implement complex caching prematurely.

Performance optimization should follow observed bottlenecks rather than speculation.

## 17. Documentation

Update `README.md` with:

- How to start Meilisearch.
- How to configure the Meilisearch URL and credentials.
- How to create or seed the development index.
- How to run Lantern.
- How to open the search page.
- How to execute tests.
- Known limitations of the prototype.

Update `docs/architecture.md` only where the implementation introduces an actual architectural decision.

Document the development search index schema.

Clearly distinguish implemented functionality from future crawling and document extraction features.

## 18. Suggested Implementation Order

Implement in the following order:

1. Inspect Phase 0 code and execute baseline tests.
2. Define minimal search response models.
3. Implement the server-side Meilisearch search service.
4. Add dependency injection and configuration.
5. Create the development seed utility.
6. Verify actual search requests against Meilisearch.
7. Implement the Blazor search form.
8. Implement result rendering.
9. Implement snippets and safe highlighting.
10. Implement URL query parameters and pagination.
11. Add loading, empty, and failure states.
12. Add automated tests.
13. Update documentation.
14. Run final build and verification.

Make incremental, logically grouped changes.

Do not restructure unrelated project code.

## 19. Acceptance Criteria

Phase 1 is complete when:

- [ ] Existing Phase 0 functionality remains operational.
- [ ] The .NET solution builds successfully.
- [ ] The Blazor application exposes a working `/search` page.
- [ ] Users can submit queries using Enter or the Search button.
- [ ] Queries execute against a real Meilisearch instance.
- [ ] Search results display titles, source information, and snippets.
- [ ] Search term highlighting is safe and functional.
- [ ] Pagination works using backend queries.
- [ ] Search state is reflected in the URL.
- [ ] Reloading a search URL preserves the query and page.
- [ ] Empty queries are handled without unrestricted searches.
- [ ] No-result searches display a useful message.
- [ ] Meilisearch connectivity failures are handled gracefully.
- [ ] API keys are never exposed to browser code.
- [ ] Unsafe source URLs are not rendered as clickable links.
- [ ] Development sample documents can be seeded repeatedly.
- [ ] Unit tests pass.
- [ ] Integration tests pass where Meilisearch is available.
- [ ] Documentation has been updated.
- [ ] No Phase 2 or later features have been implemented prematurely.

## 20. Required Completion Report

After implementation, provide the following report.

### Implemented Features

Summarize the working search functionality.

### Files Changed

List the major files created or modified.

### Architecture Decisions

Describe the chosen search service, configuration, and UI patterns.

### Build and Test Results

Include:

- Commands executed.
- Build result.
- Unit test results.
- Integration test results.
- UI verification results, if performed.

Do not claim successful execution of any command that was not run.

### Functional Verification

Report whether the following were tested:

- Normal search.
- No-result search.
- Pagination.
- Search URL restoration.
- Meilisearch unavailable.
- Unsafe URL handling.
- Japanese search.

### Known Limitations

Identify incomplete functionality and any environment-dependent verification gaps.

### Phase 2 Readiness

Explain whether the prototype provides a suitable foundation for the production search and indexing model planned in Phase 2.

## 21. Final Instructions

Implement Phase 1 now.

Use the existing Phase 0 repository as the foundation.

Produce a functioning Blazor search page connected to real Meilisearch data.

Keep the user interface simple, conventional, and readable.

Maintain a clear separation between server-side search integration and presentation components.

Run available tests and report actual results.

Do not stop after writing an implementation plan.

Do not begin Phase 2.

The expected outcome is a working, testable search UI prototype ready for further development.
