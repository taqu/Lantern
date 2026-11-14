# Lantern — Implement OpenDocument Format (ODF) Extraction Support

## Objective

Extend Lantern's document extraction pipeline to support OpenDocument Format (ODF) files on both Windows and Linux.

Implement reliable text extraction and structural metadata preservation for:

- `.odt` — OpenDocument Text
- `.ods` — OpenDocument Spreadsheet
- `.odp` — OpenDocument Presentation

Optionally support the corresponding template formats:

- `.ott` — OpenDocument Text Template
- `.ots` — OpenDocument Spreadsheet Template
- `.otp` — OpenDocument Presentation Template

The implementation must integrate with Lantern's existing document crawling, extraction, normalization, chunking, indexing, and search architecture.

The primary goal is **searchable content extraction**, not document rendering or editing.

Do not introduce mandatory dependencies on LibreOffice, OpenOffice, Microsoft Office, Java, or external conversion services.

The implementation must work offline on Windows x64 and Linux x64.

---

# Phase 1 — Inspect the Existing Extraction Architecture

## 1. Review the Repository

Before writing code, inspect:

- Document extraction interfaces.
- Extractor registry and format detection.
- Existing DOCX, XLSX, and PPTX extractors.
- PDF extraction.
- Legacy Office extraction.
- Text normalization.
- Structural metadata models.
- Source location models.
- Chunking services.
- Parent document indexing.
- Chunk indexing, if implemented.
- Meilisearch integration.
- File crawler.
- Web crawler.
- Error handling.
- Existing extraction tests.
- Supported format documentation.

Determine how extractors currently represent:

- Document title.
- Text content.
- Sections.
- Paragraphs.
- Tables.
- Worksheets.
- Slides.
- Source locations.
- Document metadata.

Reuse the existing normalized extraction model.

Do not introduce a parallel indexing pipeline for ODF.

## 2. Evaluate the Implementation Strategy

ODF is based on XML documents packaged in a ZIP container.

Evaluate whether the existing implementation can support ODF using:

```text
System.IO.Compression
System.Xml
System.Xml.Linq
```

Prefer built-in .NET functionality when it can meet the requirements.

A third-party ODF library may be adopted if it provides substantial advantages in correctness, compatibility, or maintainability.

Before introducing a dependency, verify:

- .NET 10 compatibility.
- Windows and Linux compatibility.
- Maintenance status.
- License and redistribution requirements.
- XML and ZIP security behavior.
- Actual ODT, ODS, and ODP extraction coverage.

Do not introduce a library solely because it advertises ODF support without validating extraction quality.

## 3. ODF Standards Compatibility

Use the official OASIS OpenDocument specification as the primary reference.

Target normal packaged ODF documents, including commonly encountered ODF 1.2 and 1.3 files.

Investigate compatibility with newer documents without claiming untested version support.

Relevant package entries include:

```text
mimetype
content.xml
styles.xml
meta.xml
settings.xml
META-INF/manifest.xml
```

Not every package entry is required for every extraction scenario.

Do not assume a fixed set of ZIP entries without checking the actual package format.

Support valid documents produced by multiple ODF-compatible applications.

---

# Phase 2 — Shared ODF Package Reader

## 4. Implement an ODF Package Reader

Create a reusable component responsible for safely opening ODF packages.

Responsibilities:

1. Validate the input file.
2. Open the ZIP container.
3. Identify the ODF document type.
4. Inspect package metadata.
5. Detect encrypted content.
6. Read the required XML entries.
7. Enforce resource limits.
8. Return structured XML content to the format-specific extractor.

Conceptual structure:

```text
OpenDocumentPackageReader
    |
    +-- Package Validation
    +-- MIME Type Detection
    +-- Manifest Inspection
    +-- Encryption Detection
    +-- XML Loading
    +-- Resource Limits
```

Reuse the existing ZIP security infrastructure if available.

Do not duplicate decompression code unnecessarily.

## 5. MIME Type Detection

Recognize the appropriate MIME types.

Examples:

```text
application/vnd.oasis.opendocument.text
application/vnd.oasis.opendocument.spreadsheet
application/vnd.oasis.opendocument.presentation
```

Validate both the extension and the actual package content.

Do not blindly trust the filename extension.

Handle:

- Valid ODF packages.
- Incorrect extensions.
- Renamed ZIP files.
- Malformed ZIP files.
- Missing required content.
- Unsupported ODF variants.
- Encrypted documents.

Avoid routing unrelated ZIP-based formats to an ODF extractor.

## 6. XML Namespace Handling

ODF uses multiple XML namespaces.

Implement namespace-aware parsing for relevant elements and attributes.

Examples include:

```text
office
text
table
draw
presentation
style
meta
dc
```

Do not parse ODF XML by matching unqualified element names.

Do not strip namespaces before processing.

Use namespace constants or a centralized namespace registry.

## 7. Secure XML Processing

Treat ODF documents as untrusted input.

Requirements:

- Disable unsafe external entity resolution.
- Prohibit DTD processing unless a documented safe use is required.
- Prevent external resource loading.
- Limit document size.
- Limit XML depth and processed node counts where appropriate.
- Support cancellation.
- Avoid uncontrolled memory allocation.
- Avoid loading unnecessary package entries.

Prefer streaming parsing when it materially reduces memory consumption.

Do not rely solely on compressed ZIP file size when enforcing limits.

## 8. ZIP Security

Protect against:

- ZIP bombs.
- Excessive decompression ratios.
- Excessive entry counts.
- Oversized XML entries.
- Invalid entry paths.
- Duplicate or ambiguous entry names.
- Malformed ZIP structures.
- Unexpected nested archives.

Do not extract archive entries to arbitrary filesystem paths.

For ordinary XML processing, prefer reading directly from ZIP entry streams.

Apply the existing document extraction resource limits.

---

# Phase 3 — ODT Text Extraction

## 9. Implement ODT Extraction

Create or extend an extractor for:

```text
.odt
```

Extract meaningful document text while preserving structure.

Support:

- Main document body.
- Paragraphs.
- Headings.
- Lists.
- Tables.
- Text spans.
- Explicit spaces.
- Tabs.
- Line breaks.
- Hyperlink display text.
- Section boundaries where available.

Preserve document order.

## 10. Correct Text Reconstruction

Do not concatenate all XML text nodes directly.

ODF represents some whitespace and formatting through structural elements.

Handle relevant constructs such as:

```text
text:p
text:h
text:span
text:s
text:tab
text:line-break
```

Requirements:

- Preserve paragraph boundaries.
- Preserve heading text.
- Preserve meaningful whitespace.
- Preserve list item separation.
- Preserve Japanese and English text.
- Avoid duplicate text.
- Avoid introducing excessive whitespace.

Do not treat style metadata as ordinary document text.

## 11. ODT Structural Metadata

Preserve useful structure in the normalized extraction result.

Examples:

- Heading text.
- Heading level.
- Section path.
- Paragraph order.
- Table location.

Use the existing metadata schema.

Do not invent physical page numbers when the source format does not provide reliable page pagination.

Page layout may depend on the rendering application.

The extraction goal is logical document structure, not visual pagination.

## 12. ODT Special Content

Investigate handling of:

- Headers and footers.
- Footnotes.
- Endnotes.
- Comments or annotations.
- Text frames.
- Embedded objects.
- Fields.

Prioritize extracting ordinary visible text correctly.

For secondary content, adopt a documented policy:

- Extract where reliably supported.
- Preserve origin metadata.
- Avoid duplicate indexing.
- Skip unsupported content safely.

Do not execute embedded scripts or macros.

Do not recursively process embedded objects without explicit limits.

---

# Phase 4 — ODS Spreadsheet Extraction

## 13. Implement ODS Extraction

Create or extend an extractor for:

```text
.ods
```

Extract:

- Worksheet names.
- Worksheet order.
- Cell text.
- Numeric values.
- Boolean values.
- Dates.
- Times.
- Formula cached results where available.
- Cell positions.
- Table structure.

Preserve worksheet and row boundaries.

Do not flatten the entire workbook into one unstructured string without source metadata.

## 14. Handle Repeated Rows and Columns

ODF supports compact representation of repeated cells, rows, and columns.

Relevant attributes include:

```text
table:number-columns-repeated
table:number-rows-repeated
```

These attributes can represent very large logical ranges.

### Critical requirement

Do not blindly expand repeated rows or cells into large in-memory collections.

Implement bounded iteration.

Apply limits to:

- Logical row count.
- Logical column count.
- Extracted cells.
- Output text size.
- Processing time.
- Memory usage.

Handle repeated empty cells efficiently.

Do not emit thousands of identical empty cells.

Preserve correct logical cell coordinates.

## 15. Cell Value Handling

Investigate the ODF cell value model.

Possible types include:

```text
string
float
boolean
date
time
currency
percentage
```

Use the stored value and display text appropriately.

### Requirements

- Preserve actual cell text.
- Avoid losing numeric values.
- Handle dates and times consistently.
- Preserve Japanese text.
- Use invariant parsing for machine-readable values.
- Avoid locale-dependent corruption.

Where display text and underlying values differ, define a consistent extraction policy.

Prioritize human-readable searchable content.

## 16. Formula Handling

Do not execute arbitrary spreadsheet formulas.

Use cached or stored results when available.

If no cached value is available:

- Report the limitation where relevant.
- Optionally retain the formula expression as searchable text if consistent with existing spreadsheet extraction.
- Do not claim formula evaluation occurred.

Do not invoke external spreadsheet applications.

Do not execute macros.

## 17. ODS Structural Metadata

Preserve:

- Worksheet name.
- Worksheet index.
- Cell address.
- Row number.
- Column number.
- Optional bounded cell range for grouped content.

Example normalized location:

```text
Document: financial-report.ods
Worksheet: Summary
Cell: C14
```

Integrate with the existing source-location model.

Do not introduce incompatible worksheet identifiers.

## 18. Sparse and Large Worksheets

Support sparse worksheets efficiently.

Avoid allocating complete two-dimensional arrays for large logical sheets.

Requirements:

- Stream or iterate cells.
- Skip irrelevant empty ranges.
- Bound repeated structures.
- Preserve meaningful coordinates.
- Support cancellation.
- Avoid excessive memory usage.

A small ODS archive must not be able to trigger enormous allocations through repeated-cell metadata.

---

# Phase 5 — ODP Presentation Extraction

## 19. Implement ODP Extraction

Create or extend an extractor for:

```text
.odp
```

Extract:

- Slide text.
- Slide order.
- Slide numbers.
- Text boxes.
- Shape text.
- Tables where supported.
- Speaker notes where available.
- Useful presentation metadata.

Maintain slide boundaries.

## 20. Slide Structure

Identify slide elements according to ODF namespaces and structure.

Preserve document order where practical.

Do not infer slide order from arbitrary ZIP entry order.

Do not treat master-page decorative content as ordinary slide content unless explicitly required.

Avoid duplicate text originating from shared templates.

## 21. Speaker Notes

Extract speaker notes when supported.

Distinguish them from ordinary slide content.

Example:

```text
Slide 4
  Content: Rendering Pipeline
  Notes: Explain the differences between forward and deferred rendering.
```

The example is conceptual.

Use the existing normalized extraction model.

Allow search results to identify whether a match came from slide content or speaker notes where the model supports this distinction.

## 22. ODP Source Metadata

Preserve:

- Slide number.
- Slide name where available.
- Shape or text-box order where useful.
- Speaker-note origin.

Do not invent coordinates or metadata that cannot be reliably extracted.

---

# Phase 6 — Template Formats

## 23. Evaluate Template Support

After ODT, ODS, and ODP are working, evaluate:

```text
.ott
.ots
.otp
```

These template formats may share the corresponding extraction logic.

However, verify:

- MIME type detection.
- Package structure.
- Content extraction.
- Metadata.
- Error handling.

Do not advertise template support until the test fixtures pass.

## 24. Additional ODF Formats

Do not make the following mandatory in this implementation:

```text
.odg
.odf
```

These may be considered separately.

Do not silently classify unsupported ODF formats as ordinary text documents.

Return a clear unsupported-format result.

---

# Phase 7 — Encryption and Error Handling

## 25. Detect Encrypted ODF Documents

ODF packages may contain encrypted entries.

Inspect the package manifest for encryption metadata.

### Requirements

- Detect encrypted content before attempting ordinary XML parsing.
- Return a classified encrypted-document error.
- Avoid misleading XML parsing exceptions.
- Do not attempt password guessing.
- Do not log document passwords.
- Do not silently return empty text.

Password-based decryption is not required for the initial implementation.

If decryption support already exists in Lantern, evaluate whether it can be reused safely.

Do not create a separate cryptographic framework merely for ODF.

## 26. Error Classification

Use existing extraction error types where possible.

Distinguish:

```text
UnsupportedFormat
UnsupportedVersion
EncryptedDocument
MalformedDocument
ResourceLimitExceeded
ExtractionFailed
```

Requirements:

- Preserve meaningful diagnostics.
- Avoid crashing the crawler.
- Avoid suppressing unexpected exceptions indiscriminately.
- Distinguish valid empty documents from extraction failures.
- Avoid repeated retries for deterministic parsing failures.

Never treat malformed input as successfully extracted empty content.

## 27. Resource Cleanup

Ensure correct ownership and disposal of:

- Input streams.
- ZIP archives.
- XML readers.
- Temporary buffers.
- Extraction result builders.

Avoid leaking file handles.

Respect cancellation.

Prevent partial extraction failures from leaving temporary files.

---

# Phase 8 — Integrate with Lantern Crawling

## 28. Register ODF Formats

Update:

- Extractor registry.
- Extension detection.
- MIME detection.
- Supported format filters.
- File crawler.
- UNC crawler.
- Web crawler.
- Documentation.

Required extensions:

```text
.odt
.ods
.odp
```

Optional extensions:

```text
.ott
.ots
.otp
```

Do not silently override user-configured crawler inclusion or exclusion rules.

## 29. Normalize Extracted Content

Use the existing normalized extraction result.

Ensure ODF content passes through the same pipeline as other supported documents.

Conceptually:

```text
ODF File
    |
    v
ODF Package Reader
    |
    v
ODT / ODS / ODP Extractor
    |
    v
Normalized Document Content
    |
    v
Structure-Aware Chunking
    |
    v
Parent and Chunk Indexing
    |
    v
Keyword / Hybrid Search
```

Do not create ODF-specific Meilisearch indexes.

Reuse the existing parent and chunk index architecture.

## 30. Chunking Integration

Preserve useful chunk boundaries.

### ODT

Prefer:

- Headings.
- Sections.
- Paragraphs.

### ODS

Prefer:

- Worksheet boundaries.
- Row groups.
- Bounded cell ranges.

### ODP

Prefer:

- Slide boundaries.
- Speaker-note boundaries.

Do not split structural units arbitrarily when meaningful boundaries are available.

Ensure the embedding input preserves sufficient context.

## 31. Parent and Chunk Indexing

Integrate ODF documents with:

- Parent document identity.
- Chunk identity.
- Source metadata.
- Categories.
- Incremental crawling.
- Document updates.
- Document deletion.
- Search result aggregation.

### Important

Inspect the implementation status of Chunk Indexing and Hybrid Search.

If they are not yet implemented, integrate ODF with the available extraction and parent indexing pipeline first.

Provide the required extension points for subsequent chunk indexing.

Do not falsely claim Hybrid Search works merely because ODF extraction succeeds.

Do not expand this task into a complete rewrite of the search engine.

## 32. Category Handling

ODF documents must support the same category assignments as other documents.

Requirements:

- Preserve parent categories.
- Propagate required category metadata to chunks.
- Support category filtering.
- Avoid re-extracting or re-embedding unchanged content solely because categories change.

Categories are not access-control policies.

---

# Phase 9 — Testing

## 33. Create Deterministic ODF Test Fixtures

Create synthetic or properly licensed fixtures.

Do not commit private documents.

### ODT fixtures

Include:

1. Simple English text.
2. Japanese text.
3. Multiple paragraphs.
4. Multiple headings.
5. Nested lists.
6. Tables.
7. Explicit spaces and tabs.
8. Line breaks.
9. Footnotes where supported.
10. Empty document.

### ODS fixtures

Include:

1. Single worksheet.
2. Multiple worksheets.
3. Japanese text.
4. Numeric cells.
5. Boolean cells.
6. Dates and times.
7. Cached formula results.
8. Repeated columns.
9. Repeated rows.
10. Large repetition counts.
11. Sparse worksheets.
12. Empty workbook.

### ODP fixtures

Include:

1. Simple presentation.
2. Multiple slides.
3. Japanese text.
4. Multiple text boxes.
5. Shape text.
6. Tables where supported.
7. Speaker notes.
8. Empty slides.
9. Shared master-page content.

### Negative fixtures

Include:

- Invalid ZIP files.
- Missing `content.xml`.
- Incorrect MIME types.
- Invalid XML.
- Encrypted ODF.
- Oversized XML.
- Excessive ZIP expansion.
- Excessive repeated-cell counts.
- Unsupported format variants.

## 34. Test Actual Extracted Content

Tests must verify meaningful results.

Do not merely assert that extraction does not throw an exception.

For example:

```text
Input:
  sample-japanese.odt

Expected:
  Correct Japanese text
  Correct paragraph boundaries
  Correct heading metadata
```

For ODS, verify worksheet names and cell addresses.

For ODP, verify slide ordering and speaker notes.

Check that extracted text is not duplicated or corrupted.

## 35. Windows Tests

Execute tests on Windows x64.

Verify:

- ODT extraction.
- ODS extraction.
- ODP extraction.
- Japanese text.
- Metadata.
- Cancellation.
- Malformed-file handling.
- Crawler integration.

Do not rely exclusively on mocked package readers.

## 36. Linux Tests

Execute the same tests on the available Linux environment.

Verify:

- ZIP handling.
- XML parsing.
- Character encoding.
- File permissions.
- Stream disposal.
- Large and malformed input handling.
- Crawler integration.

Do not assume .NET managed-code portability is sufficient proof of correct Linux behavior.

## 37. End-to-End Indexing Tests

Use real Meilisearch with authentication enabled.

For each required ODF format:

1. Discover the document.
2. Select the correct extractor.
3. Extract the text.
4. Preserve structural metadata.
5. Normalize the content.
6. Index the parent document.
7. Index chunks if the chunk pipeline is available.
8. Execute keyword search.
9. Execute hybrid search if implemented and available.
10. Verify source locations.
11. Recrawl without duplicates.
12. Modify the document.
13. Verify incremental synchronization.
14. Delete the document.
15. Verify index cleanup.

Do not silently skip missing integrations.

Report tests that cannot run because dependent features are not yet implemented.

## 38. Performance and Security Tests

Test:

- Large ODT documents.
- Large ODS workbooks.
- Large ODP presentations.
- High ZIP compression ratios.
- Deep XML nesting.
- Repeated spreadsheet cells.
- Cancellation during extraction.
- Concurrent extraction.
- File-handle cleanup.
- Memory limits.

Avoid relying on fixed timing assertions that are unstable across test machines.

---

# Phase 10 — Dependencies and Packaging

## 39. Avoid Unnecessary Runtime Dependencies

Prefer a self-contained .NET implementation when practical.

Do not require:

- Microsoft Office.
- LibreOffice.
- OpenOffice.
- Java.
- Docker.
- Cloud document conversion.
- External document extraction services.

If a third-party ODF library is selected, justify its use.

Report:

- Package name.
- Version.
- License.
- Maintenance status.
- Supported formats.
- Cross-platform compatibility.
- Redistribution requirements.

## 40. Windows and Linux Packaging

Verify ODF extraction in published builds.

Targets:

```text
win-x64
linux-x64
```

Ensure any necessary dependencies are included.

Do not require an additional runtime installation beyond Lantern's documented deployment requirements.

---

# Phase 11 — Documentation

## 41. Update Supported File Formats

Update the README and supported-format documentation.

Add:

```text
OpenDocument Text          .odt
OpenDocument Spreadsheet   .ods
OpenDocument Presentation  .odp
```

Add template formats only if implemented and verified.

Distinguish supported, partially supported, and unsupported features.

## 42. Update English and Japanese Documentation

Update the existing documentation structure.

Include:

- ODF support overview.
- Supported extensions.
- Extraction behavior.
- Structural metadata.
- Spreadsheet limitations.
- Speaker-note handling.
- Encrypted file behavior.
- Resource limits.
- Known compatibility issues.

Keep English and Japanese content consistent.

Update navigation and index pages.

## 43. Update Developer Documentation

Explain:

- ODF package reader.
- Namespace-aware XML parsing.
- ODT extraction.
- ODS extraction.
- ODP extraction.
- Resource limits.
- Test fixture creation.
- Cross-platform verification.

Do not document unimplemented functionality as completed.

---

# Phase 12 — Acceptance Criteria

The task is complete when:

- `.odt` is recognized and extracted.
- `.ods` is recognized and extracted.
- `.odp` is recognized and extracted.
- Japanese and English text are preserved.
- ODT paragraphs and headings are preserved.
- ODS worksheet names and cell positions are preserved.
- ODS repeated rows and columns are handled safely.
- ODP slide boundaries and numbers are preserved.
- Speaker notes are extracted where supported.
- XML namespaces are handled correctly.
- Malformed ZIP and XML files are handled safely.
- Encrypted ODF documents are detected and classified.
- Extraction resource limits are enforced.
- Existing extraction formats remain functional.
- ODF documents integrate with the existing crawler.
- Parent document indexing works.
- Chunk indexing works if the dependent feature is implemented.
- Search results preserve source metadata.
- Windows tests pass.
- Linux tests pass.
- Real Meilisearch integration tests pass.
- Documentation accurately describes the implementation.

Do not claim optional template support without successful tests.

Do not claim complete ODF specification coverage.

---

# Required Completion Report

## A. Architecture

Describe the selected ODF extraction approach.

Explain whether built-in .NET libraries or third-party packages were used.

## B. Format Support

Report actual support for:

- ODT.
- ODS.
- ODP.
- OTT, if implemented.
- OTS, if implemented.
- OTP, if implemented.

## C. Extracted Metadata

Describe the structural metadata produced by each extractor.

## D. Security

Explain:

- ZIP resource limits.
- XML parsing protections.
- Encrypted document detection.
- Cancellation.
- Memory limits.

## E. Files Changed

List source files, tests, dependencies, and documentation.

## F. Tests

Report actual test commands and results on Windows and Linux.

Distinguish passed, failed, skipped, and unexecuted tests.

## G. End-to-End Verification

Describe actual crawling, indexing, and search results for ODT, ODS, and ODP.

## H. Limitations

Document:

- Unsupported ODF features.
- Encrypted document limitations.
- Embedded object limitations.
- Formula evaluation limitations.
- Unverified ODF versions.
- Remaining search integration work.

---

# Final Instructions

Implement OpenDocument Format support in Lantern.

Prioritize `.odt`, `.ods`, and `.odp`.

Use a secure, namespace-aware ODF package reader and format-specific extractors.

Prefer built-in .NET ZIP and XML libraries where practical.

Preserve text structure and meaningful source locations.

Handle ODS repeated cells and rows without uncontrolled memory expansion.

Detect encrypted documents and report them accurately.

Do not execute macros, formulas, or embedded objects.

Integrate with the existing crawler, normalized extraction model, parent indexing, and available chunk indexing architecture.

Support Windows and Linux without requiring external Office applications.

Add meaningful test fixtures, malformed-input tests, resource-limit tests, and real indexing integration tests.

Update README and English/Japanese documentation.

**The primary success condition is that Lantern can reliably crawl, extract, index, and search meaningful content from ODT, ODS, and ODP documents on Windows and Linux, preserving useful document structure and operating safely on untrusted files.**
