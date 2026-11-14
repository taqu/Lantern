# Lantern — Fix Legacy DOC/PPT Extraction and Implement XLS Support

## Objective

Investigate and correct Lantern's legacy Microsoft Office document extraction implementation.

The current implementation uses:

https://github.com/openmcdf/openmcdf

However:

- Legacy `.doc` extraction throws exceptions.
- Legacy `.ppt` extraction throws exceptions.
- Legacy `.xls` extraction is not implemented.

The goal is to reliably extract searchable text and structural metadata from Microsoft Office 97–2003 binary formats on Windows and Linux.

**Do not assume that OpenMcdf provides complete Microsoft Office document parsing.**

OpenMcdf is a Compound File Binary Format (CFBF/OLE Structured Storage) implementation. It exposes storages and streams inside compound files. The Word, PowerPoint, and Excel binary formats require additional format-specific parsing.

The implementation must be corrected at the appropriate architectural layer rather than merely suppressing exceptions.

---

## 1. Audit the Existing Legacy Office Extraction

Inspect the repository before making changes.

Locate:

- The document extractor interface.
- The legacy `.doc` extractor.
- The legacy `.ppt` extractor.
- OpenMcdf initialization and stream access.
- Format detection and extractor registration.
- Document metadata extraction.
- Text normalization.
- Chunking integration.
- Crawler error handling.
- Existing extraction tests.
- NuGet dependencies and package versions.

Determine whether `.doc` and `.ppt` are currently implemented using:

1. OpenMcdf storage access only.
2. Manual parsing of Office-specific binary records.
3. A third-party parser layered on OpenMcdf.
4. A combination of these techniques.

Record the actual architecture and failure points.

Do not replace functioning components without a demonstrated reason.

## 2. Reproduce the Existing Exceptions

Reproduce the `.doc` and `.ppt` failures using deterministic test files.

Capture:

- Exception type.
- Complete stack trace.
- Relevant source code location.
- OpenMcdf version.
- File format classification.
- Compound storage or stream being accessed.
- Whether failure occurs during container opening or document parsing.

Distinguish the following failure categories:

- Invalid CFBF header.
- Corrupt compound file.
- Missing required stream.
- Unsupported stream layout.
- Incorrect OpenMcdf API usage.
- Invalid stream offsets.
- Unsupported Office document version.
- Text decoding error.
- Format-specific parser defect.
- Incorrect extractor dispatch.

Do not treat every parsing failure as file corruption.

Do not make assumptions about the exceptions without reproducing them.

## 3. Verify OpenMcdf Version and API Usage

Inspect the installed OpenMcdf version.

OpenMcdf v3 introduced a rewritten API.

Verify that Lantern does not mix APIs or examples from incompatible versions.

For the selected version, confirm:

- Correct method for opening files read-only.
- Correct storage and stream enumeration.
- Correct stream lookup.
- Correct stream reading.
- Correct disposal.
- Correct handling of stream positions.
- Correct nested storage traversal.
- Correct exception handling.

Where applicable, use the current supported API rather than outdated examples.

### Important

Successful CFBF parsing does not imply successful Word, PowerPoint, or Excel text extraction.

Do not attempt to fix Office-specific parsing defects by changing OpenMcdf storage handling without evidence.

## 4. Evaluate the Extraction Strategy

Before implementing format-specific parsers, investigate suitable managed .NET libraries.

Evaluate candidates for:

| Format | Required parser functionality |
|---|---|
| DOC | Word Binary File Format / HWPF-equivalent |
| PPT | PowerPoint Binary File Format / HSLF-equivalent |
| XLS | Excel BIFF / HSSF-equivalent |

Possible candidates include:

- Existing OpenMcdf-based code, if a correct and maintainable parser already exists.
- NPOI, subject to actual format support and licensing.
- Other maintained managed .NET libraries.
- A narrowly scoped, specification-based parser if no acceptable dependency exists.

### Mandatory evaluation criteria

- Real text extraction support.
- .NET 10 compatibility.
- Windows x64 compatibility.
- Linux x64 compatibility.
- Japanese text handling.
- Metadata and structural extraction.
- Security and malformed-file handling.
- Offline execution.
- No Microsoft Office installation.
- No Java runtime.
- No mandatory external process.
- License compatibility with Lantern.
- Redistribution requirements.
- Maintenance status.

### NPOI licensing warning

Inspect the actual license and EULA shipped with the proposed package version.

Some recent NPOI releases include additional maintenance-fee conditions.

Do not assume that the repository's Apache-2.0 designation alone establishes unrestricted use of every binary package release.

Do not introduce a dependency that creates incompatible obligations for Lantern's intended OSS distribution.

If licensing is ambiguous, record the issue and evaluate alternatives.

### Selection requirement

Choose the simplest maintainable implementation that can be legally distributed with Lantern.

OpenMcdf may remain useful as a CFBF container reader, but it must not be treated as a complete DOC/PPT/XLS document parser.

Do not implement full Microsoft Office binary specifications from scratch unless the benefits clearly justify the complexity.

---

# Part A — DOC Extraction

## 5. Investigate and Fix DOC Parsing

Legacy `.doc` files commonly use OLE Compound File Binary Format with Word-specific streams.

Potential streams include:

```text
WordDocument
0Table
1Table
Data
```

Actual stream requirements depend on the document version and format.

### Required investigation

Determine whether the current implementation correctly handles:

- WordDocument stream.
- File Information Block.
- Table stream selection.
- Text piece table.
- Character offsets.
- Compressed text.
- Unicode text.
- Document version variations.

Do not assume that the WordDocument stream contains ordinary contiguous plaintext.

### Critical requirement

A raw string scan of compound streams is not an acceptable general-purpose DOC parser.

The extractor must account for the Word binary format's text storage and encoding mechanisms.

## 6. Required DOC Features

Implement:

- Main document text.
- Paragraph boundaries.
- Japanese text.
- English text.
- Tables where supported.
- Headers and footers where supported.
- Useful document properties.

Preserve heading and section information when reliably available.

Do not manufacture structural metadata when the parser cannot recover it.

### Error handling

Distinguish:

- Unsupported document version.
- Encrypted document.
- Malformed Word structures.
- Missing mandatory streams.
- Resource limits exceeded.

Do not execute macros or embedded content.

---

# Part B — PPT Extraction

## 7. Investigate and Fix PPT Parsing

Legacy `.ppt` files use PowerPoint-specific binary records within compound storage.

Typical streams may include:

```text
PowerPoint Document
Current User
Pictures
```

Actual contents vary by presentation.

Determine whether the current extractor handles:

- Record headers.
- Nested records.
- Persist directory information.
- Slide references.
- Text records.
- Unicode text.
- Compressed text where applicable.
- Slide ordering.

### Critical requirement

Do not assume that PowerPoint text can be reliably extracted by searching every stream for UTF-16 strings.

The implementation must use a format-aware parser.

## 8. Required PPT Features

Implement:

- Slide text.
- Slide ordering.
- Slide numbers.
- Multiple text boxes.
- Japanese text.
- English text.
- Speaker notes where supported.
- Useful presentation metadata.

Preserve slide boundaries in the extracted document model.

### Example metadata

```text
Document: presentation.ppt
Slide: 5
Text: Example slide content
```

Use the actual extraction metadata schema already defined by Lantern.

Do not introduce a separate incompatible model.

### Error handling

Handle:

- Missing streams.
- Unsupported record types.
- Malformed record lengths.
- Truncated presentations.
- Encrypted presentations.
- Embedded objects.

Do not execute macros or embedded objects.

---

# Part C — XLS Extraction

## 9. Implement the Missing XLS Extractor

Add extraction support for legacy `.xls` workbooks.

Support Excel 97–2003 BIFF8 files as the primary target.

Determine whether older BIFF variants are supported by the chosen library.

Do not claim support for every historical XLS variant without tests.

### Important

Some legacy Excel formats may not be CFBF containers.

Use content-based format detection where practical.

Do not assume every `.xls` file can be opened through OpenMcdf.

## 10. Required XLS Features

Extract:

- Worksheet names.
- Worksheet order.
- Text cells.
- Numeric cells.
- Boolean cells.
- Date cells.
- Formula cached results, where available.
- Cell positions.
- Useful workbook metadata.

Preserve worksheet and row boundaries.

### Example

```text
Document: report.xls
Worksheet: Summary
Cell: B12
Value: Annual Revenue
```

### Formula handling

Do not execute arbitrary formulas.

Prefer stored or cached values.

If the cached value is missing, use a documented fallback.

Do not claim that formula evaluation is supported unless explicitly implemented and tested.

### Resource limits

Limit:

- Maximum processed worksheets.
- Maximum processed rows.
- Maximum processed cells.
- Maximum output text.
- Maximum extraction time or equivalent cancellation boundary.
- Memory consumption.

Handle sparse workbooks efficiently.

Do not allocate enormous structures solely because a workbook reports a large used range.

## 11. XLS Format Detection

Register `.xls` in the existing extractor registry.

Verify dispatch for:

- CFBF-based XLS.
- Unsupported older BIFF formats.
- HTML files renamed `.xls`.
- XML Spreadsheet files renamed `.xls`.
- Corrupt files.
- Password-protected files.

Use signature and structural validation where practical.

Do not route incompatible content into a parser simply because its extension is `.xls`.

---

# Part D — Shared Extraction Architecture

## 12. Use Existing Extractor Interfaces

Integrate the three formats through the existing document extraction abstraction.

Conceptually:

```text
DocumentExtractor
    |
    +-- ModernOfficeExtractor
    |       +-- DOCX
    |       +-- XLSX
    |       +-- PPTX
    |
    +-- LegacyOfficeExtractor
            +-- DOC
            +-- XLS
            +-- PPT
```

The actual structure should follow the existing repository architecture.

Avoid duplicating crawler or indexing logic.

All extractors should return the existing normalized extraction result type.

## 13. Preserve Structural Metadata

Legacy Office extraction must integrate with the existing chunking and search pipeline.

Preserve:

| Format | Structure |
|---|---|
| DOC | Paragraphs and sections |
| XLS | Worksheets, rows, and cell locations |
| PPT | Slides and speaker notes |

Use the same source-location model used by PDF, modern Office, and OpenDocument extraction where possible.

### Requirements

- Text must remain searchable.
- Chunk boundaries should respect document structure.
- Source locations must be retained.
- Parent document association must remain stable.
- Reindexing must be idempotent.

Do not change unrelated search schemas solely to support legacy Office files.

## 14. Register Supported Extensions

Update:

- File extension classification.
- MIME handling.
- Extractor selection.
- Local file crawler.
- UNC crawler.
- Web crawler download handling.
- Documentation.
- Supported format tests.

Ensure `.doc`, `.xls`, and `.ppt` are recognized.

Do not mark a format as supported if it still throws exceptions for normal valid documents.

---

# Part E — Security and Reliability

## 15. Treat Office Files as Untrusted Input

Legacy binary document parsers are complex and must be treated as security-sensitive components.

Requirements:

- Validate file headers.
- Validate stream bounds.
- Validate record lengths.
- Bound memory usage.
- Bound extracted content size.
- Support cancellation.
- Prevent recursive embedded-object processing.
- Avoid macro execution.
- Avoid launching external programs.
- Avoid network access during extraction.

Do not accept unbounded allocations based on values supplied by the document.

## 16. Exception Handling

Use meaningful extraction failure classifications.

Suggested categories:

```text
UnsupportedFormat
UnsupportedVersion
EncryptedDocument
MalformedDocument
ResourceLimitExceeded
ExtractionFailed
```

Adapt to the existing error model.

### Requirements

- Preserve diagnostic stack traces in protected logs.
- Provide concise crawler-level error summaries.
- Do not crash the entire crawler because one document is malformed.
- Do not silently return an empty document after parser failure.
- Distinguish a genuinely empty valid document from failed extraction.
- Avoid repeated expensive retries for deterministic parse failures.

### Important

Do not fix the existing exceptions with broad:

```csharp
catch (Exception)
{
    return string.Empty;
}
```

The parser must either produce meaningful extracted content or report a classified failure.

## 17. Resource Ownership

Verify correct disposal of:

- Input file streams.
- Compound storage objects.
- Parser instances.
- Temporary buffers.
- Extracted data structures.

Do not dispose the input stream before the parser has completed.

Check whether the selected parser takes ownership of its input stream.

Avoid leaking file handles during recursive crawling.

---

# Part F — Tests

## 18. Create Legacy Office Fixtures

Create or obtain redistributable test fixtures for each supported format.

At minimum:

### DOC

- Simple English text.
- Japanese text.
- Multiple paragraphs.
- Table.
- Headers and footers where supported.
- Empty document.

### PPT

- Simple presentation.
- Japanese text.
- Multiple slides.
- Multiple text boxes.
- Speaker notes where supported.
- Empty slides.

### XLS

- Simple workbook.
- Multiple worksheets.
- Japanese text.
- Text and numeric cells.
- Dates.
- Formulas with cached results.
- Sparse workbook.
- Empty workbook.

### Negative fixtures

- Truncated CFBF file.
- Incorrect extension.
- Missing required stream.
- Invalid record length.
- Encrypted document.
- Unsupported legacy version.
- Oversized input.

Do not commit proprietary user documents to the repository.

Use synthetic or appropriately licensed fixtures.

## 19. Exact Extraction Assertions

Tests must verify actual extracted text.

Do not merely assert that no exception was thrown.

For example:

```text
Input:
  legacy-japanese.doc

Expected:
  Japanese text appears correctly.
  Paragraph boundaries are preserved.
  No replacement-character corruption.
```

For PPT, verify slide ordering.

For XLS, verify worksheet names and cell positions.

## 20. Windows Integration Tests

Run on Windows.

Verify:

- DOC extraction.
- PPT extraction.
- XLS extraction.
- Japanese text.
- Structural metadata.
- Error classification.
- Resource cleanup.
- Crawler integration.

Use the actual selected libraries.

## 21. Linux Integration Tests

Run on the available Linux environment.

Verify the same extraction scenarios.

Do not assume pure managed code guarantees identical behavior across platforms.

Pay attention to:

- Encoding support.
- File access.
- Case sensitivity.
- Package dependencies.
- Runtime resource behavior.

Do not introduce Windows-only COM dependencies.

## 22. End-to-End Indexing Tests

Use real Meilisearch.

For DOC, PPT, and XLS:

1. Discover document.
2. Select correct extractor.
3. Extract text.
4. Preserve metadata.
5. Normalize content.
6. Generate chunks where implemented.
7. Index parent document.
8. Index chunks where implemented.
9. Search for extracted text.
10. Verify result source location.
11. Recrawl without duplicates.
12. Update the source document.
13. Verify index synchronization.
14. Delete the source document.
15. Verify cleanup.

### Important

If Chunk Indexing or Hybrid Search is still under development, clearly distinguish completed integration tests from pending features.

Do not claim hybrid-search validation based only on keyword indexing.

---

# Part G — Dependencies and Packaging

## 23. Dependency Review

For every new package, report:

- Package name.
- Version.
- License and additional terms.
- Transitive dependencies.
- .NET compatibility.
- Windows compatibility.
- Linux compatibility.
- Maintenance status.
- Security considerations.
- Packaging implications.

Avoid dependency proliferation.

Do not bundle Java, LibreOffice, Microsoft Office, or an external document conversion server.

### Licensing

Lantern is intended to remain an OSS project.

Do not introduce a dependency whose redistribution or usage conditions conflict with Lantern's licensing and distribution goals.

If a candidate library is unsuitable, explain why and evaluate a different approach.

## 24. Release Verification

Verify self-contained packaging on:

- Windows x64.
- Linux x64.

Ensure all required managed and native dependencies are included.

Verify extraction using the published application, not only unit tests.

---

# Part H — Documentation

## 25. Update Supported Format Documentation

Update:

- README.
- English documentation.
- Japanese documentation.
- File crawler documentation.
- Extraction architecture documentation.
- Developer documentation.

Document actual support for:

```text
.doc
.xls
.ppt
```

Explain:

- Supported Office generations.
- Unsupported historical variants.
- Encrypted document behavior.
- Metadata extraction limitations.
- Embedded-object limitations.
- Known malformed-file behavior.

Do not claim complete Microsoft Office compatibility.

## 26. Update Implementation Documentation

Describe the selected extraction architecture.

Explain the separation between:

- CFBF container access.
- DOC binary parsing.
- XLS BIFF parsing.
- PPT binary parsing.
- Normalized extraction output.
- Indexing integration.

Document why OpenMcdf alone is insufficient for complete Office text extraction.

---

# Part I — Acceptance Criteria

The task is complete when:

- The existing DOC exceptions have been reproduced and their root causes identified.
- Valid DOC documents produce searchable text.
- Valid PPT documents produce searchable text.
- XLS extraction is implemented.
- Japanese content is preserved.
- DOC paragraph boundaries are retained where supported.
- PPT slide ordering and slide numbers are retained.
- XLS worksheets and cell locations are retained.
- The extractor registry recognizes all three formats.
- Incorrect formats are rejected safely.
- Malformed documents cannot crash the crawler.
- No macros or embedded programs are executed.
- Windows tests pass.
- Linux tests pass.
- End-to-end indexing tests pass.
- All dependencies satisfy project licensing requirements.
- Existing extraction formats continue to work.
- Documentation accurately describes supported capabilities.

Do not declare success if DOC and PPT merely stop throwing exceptions but fail to extract the expected text.

---

# Required Completion Report

## A. Root Causes

Identify the exact reasons DOC and PPT extraction previously threw exceptions.

Include the relevant parser or stream operations.

## B. Library Selection

Explain whether OpenMcdf was retained, supplemented, or replaced.

List all selected libraries and their licensing conditions.

## C. Format Support

Report actual support for:

- DOC.
- XLS.
- PPT.

Describe limitations separately for each format.

## D. Implementation

Summarize the extractor implementations and integration with the existing document model.

## E. Files Changed

List source files, tests, dependencies, and documentation updated.

## F. Test Results

Report actual commands executed on Windows and Linux.

Distinguish passed, failed, skipped, and unexecuted tests.

## G. Real-World Verification

Report whether the previously failing DOC and PPT documents were successfully processed, if those files are available in the development environment.

Do not include proprietary file contents in the repository or logs.

## H. Remaining Limitations

Identify unsupported document variants, unresolved parser defects, and any incomplete integration work.

---

## Final Instructions

Investigate the current OpenMcdf-based DOC and PPT extractors and reproduce their exceptions.

Determine whether each failure originates in CFBF stream handling or incorrect format-specific binary parsing.

Do not assume OpenMcdf is a complete legacy Office parser.

Evaluate maintained, cross-platform .NET libraries for Word Binary, PowerPoint Binary, and Excel BIFF formats.

Verify package licensing before adding dependencies.

Correct DOC and PPT text extraction and implement the missing XLS extractor.

Preserve Japanese text and document structure.

Integrate all three formats with Lantern's existing crawler, extraction, indexing, and search infrastructure.

Use safe parsing, bounded resource consumption, meaningful error handling, and deterministic test fixtures.

Verify functionality on Windows and Linux.

Update the README and English/Japanese documentation.

**The primary success condition is that Lantern can reliably extract and index meaningful text from valid legacy DOC, XLS, and PPT documents without requiring Microsoft Office, Java, LibreOffice, or an external conversion service.**
