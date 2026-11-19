# Lantern — Add Copy Path, Directory Path, and URL Actions to Search Results

## Objective

Enhance Lantern's search results UI with convenient clipboard actions.

Users should be able to copy the original source location of a search result without manually selecting or editing the displayed path.

Required functionality:

- **File system results:** Copy the full file path.
- **File system results:** Copy the parent directory path.
- **Web results:** Copy the original URL.

The implementation must support Windows, Linux, and UNC network paths.

This is a focused usability enhancement. Do not redesign the search engine, crawler architecture, or indexing pipeline unnecessarily.

---

## 1. Inspect the Existing Implementation

Before changing code, inspect:

- Blazor search result components.
- Search result view models and DTOs.
- Search services.
- Parent document metadata.
- Source type definitions.
- Local file crawler metadata.
- UNC crawler metadata.
- Web crawler metadata.
- Search result aggregation.
- Keyword search.
- Hybrid search, if implemented.
- Existing JavaScript interop services.
- UI notification or toast components.
- Existing localization infrastructure.
- Relevant tests.

Identify how Lantern currently distinguishes file system documents from web documents.

Reuse existing source metadata and UI patterns.

Do not create a new source classification scheme if one already exists.

## 2. Define Copy Actions by Source Type

Implement the following behavior:

| Source type | Copy source | Copy directory |
|---|---|---|
| Windows local file | Full Windows path | Parent Windows directory |
| Windows UNC file | Full UNC path | Parent UNC directory |
| Linux local file | Full POSIX path | Parent POSIX directory |
| Web page | Original absolute URL | Not available |

For example:

```text
Windows file:
D:\Documents\Research\presentation.ppt

Copy file path:
D:\Documents\Research\presentation.ppt

Copy directory:
D:\Documents\Research
```

```text
UNC file:
\\fileserver\shared\reports\summary.pdf

Copy file path:
\\fileserver\shared\reports\summary.pdf

Copy directory:
\\fileserver\shared\reports
```

```text
Linux file:
/home/user/documents/report.pdf

Copy file path:
/home/user/documents/report.pdf

Copy directory:
/home/user/documents
```

```text
Web page:
https://example.com/articles/overview

Copy URL:
https://example.com/articles/overview
```

Do not provide a directory-copy action for web URLs.

## 3. Search Result UI

Add an unobtrusive copy action to each search result.

### File system results

Prefer a compact menu offering:

- Copy File Path
- Copy Directory Path

### Web results

Provide a direct action:

- Copy URL

Follow the existing Lantern design system.

Avoid introducing large buttons that make search results unnecessarily tall.

Keep existing result navigation and source-opening behavior intact.

### Accessibility

Ensure:

- Controls are keyboard accessible.
- Buttons have meaningful accessible labels.
- Icons are not the only indication of their purpose.
- Focus indicators are visible.
- Copy actions do not accidentally trigger result navigation.

If result cards are clickable, prevent event propagation from copy controls where appropriate.

## 4. Use Original Source Metadata

Clipboard values must come from authoritative source metadata.

Do not copy:

- Truncated display paths.
- Highlighted search snippets.
- HTML-encoded presentation strings.
- Breadcrumb labels.
- Search result titles.

Use the original source path or URL associated with the indexed document.

### Important

Search results may originate from matching chunks rather than parent documents.

If Chunk Indexing and Hybrid Search are implemented, ensure the copy action uses the parent document's original source location.

Do not use chunk identifiers or internal Meilisearch document IDs as source paths.

## 5. Correct Path Handling

Do not assume that the client's operating system matches the source filesystem.

Lantern may run on Linux while displaying Windows paths, or vice versa.

For example, a Windows file path must remain a Windows path even when viewed from a Linux browser.

### Requirements

- Preserve Windows drive letters.
- Preserve Windows backslashes.
- Preserve UNC prefixes.
- Preserve POSIX separators.
- Preserve filenames containing spaces.
- Preserve Unicode and Japanese path components.
- Avoid unnecessary URL decoding or path rewriting.

### Parent directory calculation

Prefer computing the parent directory using source-platform-aware logic.

Do not use the host OS's path parser blindly for paths originating from another platform.

Handle:

- Drive roots.
- UNC share roots.
- POSIX root directories.
- Missing parents.
- Malformed or incomplete paths.

If no meaningful parent directory exists, disable or omit the directory-copy action.

Do not invent a parent path.

## 6. Avoid Unnecessary Schema Changes

Inspect whether the existing search result model already contains:

- Source type.
- Original path or URL.
- Source platform.
- Parent directory.

If sufficient information already exists, implement the feature without changing Meilisearch schemas.

If the original source location is not available in search results, extend the existing DTO or result aggregation appropriately.

Do not introduce a full reindex requirement merely to add clipboard actions unless strictly necessary.

Preserve compatibility with existing indexed documents.

## 7. Clipboard Integration

Use the browser Clipboard API through the application's existing JavaScript interop infrastructure.

Conceptually:

```javascript
export async function copyText(text) {
    await navigator.clipboard.writeText(text);
}
```

The implementation must detect and report failure.

### Requirements

- Use `navigator.clipboard.writeText()` where supported.
- Handle permission or security-context failures.
- Handle unavailable Clipboard API support.
- Avoid unnecessary third-party dependencies.
- Do not use obsolete clipboard techniques unless a justified compatibility fallback is needed.
- Do not expose unrelated sensitive data.

### Blazor

Use the existing Blazor JavaScript interop conventions.

Avoid duplicating clipboard services across multiple components.

Ensure the interop call is made during a valid interactive user action.

Handle component disposal and navigation correctly.

## 8. Copy Feedback

Provide brief feedback after the copy operation.

Examples:

```text
File path copied.
Directory path copied.
URL copied.
```

For failures:

```text
Could not copy to clipboard.
```

Use the existing toast or notification system.

### Requirements

- Show success only after the clipboard operation succeeds.
- Show an actionable failure message when necessary.
- Do not display the complete copied path in every notification.
- Avoid blocking dialogs.
- Do not create duplicate notifications for one click.

Keep feedback concise.

## 9. HTTP and HTTPS Compatibility

Clipboard API availability depends on browser security restrictions.

Investigate Lantern's supported deployment modes:

- HTTPS on localhost.
- HTTP on localhost.
- HTTPS on LAN.
- HTTP on LAN.

### Requirements

- Verify behavior in supported secure contexts.
- Handle unsupported contexts gracefully.
- Do not silently report success when the browser refuses clipboard access.
- Do not weaken application security to make clipboard access work.

Document limitations for LAN deployments using plain HTTP if applicable.

## 10. Localization

If Lantern supports English and Japanese UI localization, add appropriate translations.

Suggested strings:

| English | Japanese |
|---|---|
| Copy File Path | ファイルパスをコピー |
| Copy Directory Path | ディレクトリパスをコピー |
| Copy URL | URLをコピー |
| File path copied | ファイルパスをコピーしました |
| Directory path copied | ディレクトリパスをコピーしました |
| URL copied | URLをコピーしました |
| Copy failed | コピーに失敗しました |

Follow the existing localization mechanism.

Do not introduce hardcoded English-only strings if localization is already supported.

## 11. Security and Authorization

Only expose source paths and URLs already authorized for the current user.

The copy feature must not bypass existing search result access restrictions.

Do not create an endpoint that reveals arbitrary filesystem paths.

Do not read local files merely to copy their paths.

Do not disclose internal credentials, authentication tokens, or hidden source metadata.

### Web URLs

Copy the intended source URL, not an internal crawler endpoint.

Avoid copying temporary authentication credentials or internal tracking URLs unintentionally.

Preserve the existing URL handling and access-control policy.

## 12. Automated Tests

Add tests for source classification and clipboard values.

### Windows paths

Test:

- Simple paths.
- Paths containing spaces.
- Japanese filenames.
- Drive roots.
- Nested directories.
- Mixed-case paths.

### UNC paths

Test:

- Standard UNC paths.
- Nested shares.
- Share roots.
- Japanese directory names.
- Invalid or incomplete UNC paths.

### Linux paths

Test:

- Absolute POSIX paths.
- Root-level files.
- Nested directories.
- Unicode filenames.
- Paths with spaces.
- Root directory handling.

### Web URLs

Test:

- HTTP URLs.
- HTTPS URLs.
- Query strings.
- Fragment identifiers.
- Percent-encoded paths.
- Unicode URLs where supported.

Ensure web results never display the directory-copy action.

## 13. Search Integration Tests

Verify copy actions for:

- Keyword search results.
- Hybrid search results, if implemented.
- Parent document results.
- Chunk-derived results.
- Category-filtered results.
- Local file crawler results.
- UNC crawler results.
- Web crawler results.

The copied value must always refer to the original source.

Do not accidentally copy the chunk storage location or Meilisearch index identity.

## 14. UI Interaction Tests

Verify:

1. Copy File Path invokes the clipboard operation.
2. Copy Directory Path invokes the clipboard operation.
3. Copy URL invokes the clipboard operation.
4. Success feedback appears after a successful copy.
5. Failure feedback appears after a rejected clipboard operation.
6. Copying does not navigate to the search result.
7. Keyboard interaction works.
8. Missing path values disable inappropriate actions.
9. Search result rendering remains unchanged otherwise.
10. Controls work after Blazor navigation and reconnection.

Use the existing testing framework.

## 15. Windows and Linux Verification

Test the application on both supported platforms.

Include cross-platform source scenarios.

Examples:

- Lantern on Windows displaying Windows paths.
- Lantern on Linux displaying Linux paths.
- Lantern on Linux displaying an indexed Windows path.
- Lantern on Windows displaying an indexed Linux path.
- UNC paths displayed in either environment.

The copied value must preserve the source path syntax regardless of the server operating system.

## 16. Documentation Updates

Update relevant user documentation in:

```text
docs/en/
docs/ja/
```

Document:

- How to copy a file path.
- How to copy a parent directory.
- How to copy a web URL.
- Differences between file and web results.
- Clipboard security-context limitations.
- Cross-platform path behavior.

Update screenshots if the repository maintains UI screenshots.

Do not add obsolete or unimplemented commands.

## 17. Acceptance Criteria

The task is complete when:

- File search results provide Copy File Path.
- File search results provide Copy Directory Path.
- Web results provide Copy URL.
- Web results do not provide directory-copy actions.
- Original source values are copied without unwanted transformations.
- Windows paths are preserved correctly.
- UNC paths are preserved correctly.
- Linux paths are preserved correctly.
- Japanese and Unicode paths are preserved correctly.
- Chunk-derived results use the parent source location.
- Clipboard failures are handled correctly.
- UI notifications reflect actual outcomes.
- Copy actions do not interfere with navigation.
- Keyboard accessibility works.
- Existing keyword search continues to work.
- Existing hybrid search remains unaffected where implemented.
- Windows and Linux tests pass.
- English and Japanese documentation is updated.

## 18. Required Completion Report

Provide:

### A. Implementation summary

Explain how source types and copy actions are determined.

### B. Data model changes

Describe any DTO or metadata changes.

If no schema changes were necessary, state that explicitly.

### C. Clipboard implementation

Describe JavaScript interop and error handling.

### D. Path handling

Explain Windows, UNC, and Linux parent-directory handling.

### E. Files changed

List modified components, services, tests, and documentation.

### F. Tests

Report actual test commands and results.

Distinguish passed, failed, skipped, and unexecuted tests.

### G. Platform verification

Describe actual Windows and Linux testing.

### H. Known limitations

Document clipboard security-context restrictions and other unresolved issues.

---

## Final Instructions

Add copy actions to Lantern search results.

For file system results, allow users to copy both the complete file path and the parent directory path.

For web results, allow users to copy the original URL.

Reuse existing source metadata and the established Blazor UI conventions.

Preserve Windows, UNC, and Linux path syntax independently of the host operating system.

Use browser Clipboard API integration with appropriate success and failure feedback.

Do not redesign the search engine or introduce unnecessary Meilisearch schema changes.

Ensure chunk-derived results copy the original parent document location.

Support accessibility, localization, and existing authorization boundaries.

Test the feature on Windows and Linux.

Update English and Japanese documentation.

**The primary success condition is that users can reliably copy the original file path, parent directory path, or web URL directly from Lantern search results, without manual text selection and without affecting existing search functionality.**
