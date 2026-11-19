# Lantern — Fix Keyword Search Regression Introduced by Clipboard Feature

## Confirmed Facts

- Keyword Search worked correctly before the search-result clipboard feature was implemented.
- After the clipboard changes, `/search` no longer returns results normally.
- Meilisearch is running.
- The search endpoint is successfully routed.
- An outgoing HTTP operation eventually reaches the default 100-second `HttpClient.Timeout`.
- The failing request is a Keyword Search request, not a Hybrid or Semantic Search request.

## Objective

Identify and fix the regression introduced by the clipboard feature without removing the requested functionality.

**Prioritize investigating the changes made for clipboard support. Do not begin by redesigning Meilisearch, API key management, or the embedding infrastructure.**

## 1. Identify the Regression Commit

Inspect the Git history and working-tree changes.

Find the last revision where Keyword Search worked and the revision that introduced clipboard support.

Use commands such as:

```bash
git status
git log --oneline -15
git diff <working-revision>..<clipboard-revision> --stat
git diff <working-revision>..<clipboard-revision>
```

Adapt the revision range to the actual repository.

Do not discard unrelated uncommitted changes.

Inspect all changes affecting:

- Search Razor components.
- Search result rendering.
- Clipboard interop.
- JavaScript modules.
- Search result DTOs.
- Source path handling.
- Search services.
- Dependency injection.
- HttpClient configuration.

## 2. Reproduce Both Revisions

Using an isolated development environment, verify:

**Before clipboard implementation:**

```text
/search?q=ライティング&page=1
```

Expected: Keyword Search completes and displays results.

**After clipboard implementation:**

Reproduce the timeout.

Use the same Meilisearch instance, index, credentials, and query where possible.

Do not modify or reset existing search indexes.

## 3. Isolate the Clipboard Changes

Determine whether the regression is caused by:

1. Clipboard UI controls.
2. JavaScript module initialization.
3. Blazor component lifecycle changes.
4. Parent directory path calculation.
5. New search result DTO fields.
6. Additional database or HTTP requests.
7. Dependency injection changes.
8. Search result mapping.
9. A blocking or recursive asynchronous operation.

Temporarily disable individual changed components in a controlled test to identify the responsible code path.

Do not permanently remove clipboard functionality as the solution.

## 4. Instrument Search Execution

Add targeted timing diagnostics around:

```text
/search request
    |
    v
Blazor component initialization
    |
    v
Keyword Search service
    |
    v
Meilisearch HTTP request
    |
    v
Search result mapping
    |
    v
Clipboard-related data preparation
    |
    v
Razor rendering
```

For each stage, record:

- Start and completion.
- Elapsed duration.
- Cancellation.
- Exceptions.
- Relevant request correlation ID.

Never log API keys or sensitive search document contents.

## 5. Identify the Timed-Out HTTP Operation

Capture the complete stack trace of the `HttpClient.Timeout` exception.

Determine the exact outgoing endpoint and calling method.

Check whether clipboard support introduced an additional HTTP request that did not exist previously.

Do not assume the timed-out request is the main Meilisearch search request.

In particular, inspect any new service call used to retrieve original file paths, source metadata, or parent documents.

## 6. Verify Blazor Lifecycle

Check that clipboard functionality does not invoke JavaScript during static prerendering.

Verify:

- No JS invocation in `OnInitializedAsync`.
- No inappropriate JS invocation in `OnParametersSetAsync`.
- Browser-only operations occur during valid interactive lifecycle stages.
- Clipboard initialization is not required to complete the search request.
- No blocking `.Result` or `.Wait()`.
- No render loops triggered by state updates.
- No repeated search invocation caused by query parameter binding.
- Clipboard controls do not submit forms or trigger navigation.

## 7. Fix the Root Cause

Implement the smallest targeted correction.

Requirements:

- Preserve Keyword Search behavior.
- Preserve file path copying.
- Preserve directory path copying.
- Preserve URL copying.
- Preserve pagination.
- Preserve category filtering.
- Preserve Japanese query handling.
- Do not alter Meilisearch authentication unnecessarily.
- Do not increase HTTP timeouts to hide the problem.
- Do not suppress unexpected exceptions.

Prefer separating clipboard operations from search execution.

Clipboard operations should run only after a user explicitly clicks a copy action.

## 8. Regression Tests

Verify:

1. Keyword Search works before and after clipboard initialization.
2. `/search?q=ライティング&page=1` returns results.
3. Japanese query parameters work.
4. Search completes without unexplained 100-second timeouts.
5. Search results render with clipboard controls.
6. Copy File Path works.
7. Copy Directory Path works.
8. Copy URL works.
9. Clipboard failures do not affect search results.
10. Missing source paths do not crash rendering.
11. Repeated searches work.
12. Pagination works.
13. Category filters work.
14. Navigation does not cause repeated search requests.
15. No additional unnecessary backend HTTP requests are introduced.

Use real authenticated Meilisearch for end-to-end verification.

## 9. Completion Report

Report:

- Last known working revision.
- Regression-introducing change.
- Exact failing source method.
- Exact HTTP request that timed out.
- Root cause.
- Files changed.
- Tests executed and results.
- Confirmation that clipboard functionality remains available.

**The task is complete only when Keyword Search works as it did before the clipboard implementation, while the new copy actions also function correctly.**
