# Lantern Dependencies

This document describes the direct and notable transitive dependencies of Lantern,
including their licenses and redistribution considerations.

This is a technical assessment only. It is not a substitute for legal advice.

## Phase 3 additions

Administration uses Microsoft.EntityFrameworkCore.Sqlite and Design 10.0.13, plus
Microsoft.AspNetCore.Mvc.Testing 10.0.13 for HTTP tests. The SQLitePCLRaw bundle is pinned
to 3.0.5, bringing the native SQLite 3.53.4 package instead of the vulnerable older native
dependency selected by the original provider defaults. EF Core and ASP.NET Core packages
use MIT licensing; SQLitePCLRaw's package metadata specifies Apache-2.0. The existing
feasibility tool's ImageSharp warnings remain outside this phase.

Package records: [EF SQLite](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Sqlite/10.0.13),
[SQLitePCLRaw bundle](https://www.nuget.org/packages/SQLitePCLRaw.bundle_e_sqlite3/3.0.5).

---

## Meilisearch Community Edition (binary)

| Property | Value |
|----------|-------|
| Project | https://github.com/meilisearch/meilisearch |
| Releases | https://github.com/meilisearch/meilisearch/releases |
| License | SSPL v1 (Server Side Public License, version 1) |
| SPDX | SSPL-1.0 |
| Windows x64 available | Yes — `meilisearch-windows-amd64.exe` |
| Docker required | No |

### SSPL v1 Redistribution Analysis

SSPL v1 is a copyleft license written by MongoDB, Inc. It is **not OSI-approved** and is **not FSF-free**.

Key redistribution obligations under SSPL v1:

- You may run Meilisearch for any purpose, including commercially.
- You may distribute the unmodified official binary.
- If you **modify** the Meilisearch source and **provide it as a service to third parties**, you must release all software used to provide that service under SSPL v1.
- Bundling the **unmodified official binary** with a separately-licensed application (such as Lantern) does **not** trigger the service-provider clause, because Lantern ships the official binary without modification.
- You must include the SSPL license notice when distributing.

**Conclusion for Lantern:** Bundling the unmodified official Meilisearch Windows binary alongside the MIT-licensed Lantern application is permitted under SSPL v1, provided the SSPL license text is included. Lantern does not modify Meilisearch source code.

**Unresolved question:** Whether SSPL v1 is compatible with Lantern's intended MIT license for the application itself requires careful review. The Lantern application code is MIT; the Meilisearch binary is SSPL v1. These are separate works distributed together. Legal counsel is recommended before commercial redistribution.

---

## Meilisearch .NET Client (NuGet)

| Property | Value |
|----------|-------|
| NuGet ID | `Meilisearch` |
| Version used | 0.20.0 |
| NuGet URL | https://www.nuget.org/packages/Meilisearch |
| Source | https://github.com/meilisearch/meilisearch-dotnet |
| License | MIT |
| Redistribution | Permitted without restriction |

---

## Toxy

| Property | Value |
|----------|-------|
| NuGet ID | `Toxy` |
| Version used | 2.6.0 |
| NuGet URL | https://www.nuget.org/packages/Toxy |
| Source | https://github.com/nissl-lab/toxy |
| License | Apache 2.0 |
| Redistribution | Permitted |
| .NET 10 compatibility | Compatible (targets netstandard2.0 / netstandard2.1) |
| Lantern usage | Modern Office (XLSX via `XlsxExtractor`); legacy formats (.doc / .xls / .ppt) are no longer routed through Toxy |

Toxy bundles PdfPig and DocumentFormat.OpenXml as dependencies.

### Toxy Transitive Dependencies (selected)

| Package | Version | License | Notes |
|---------|---------|---------|-------|
| DocumentFormat.OpenXml | 3.4.1 | MIT | DOCX/XLSX/PPTX parsing |
| PdfPig | 0.1.13 | Apache 2.0 | PDF text extraction |
| HtmlAgilityPack | 1.12.4 | MIT | HTML parsing |
| NPOI | 2.7.5 | Apache 2.0 | Legacy XLS support (now referenced directly, see NPOI section) |
| CsvHelper | 33.1.0 | MS-PL / Apache 2.0 | CSV parsing |
| SixLabors.ImageSharp | 2.1.11 | Apache 2.0 / Six Labors Split | Image handling |
| OpenMcdf | 3.4.0 | MPL 2.0 | OLE2 container reading (now referenced directly, see OpenMcdf section) |

### Known Vulnerability Assessment (October 2026)

The following vulnerabilities are reported by `dotnet list package --vulnerable --include-transitive`
against the current dependency graph. All are transitive — they cannot be resolved without Toxy
providing updated dependencies.

| Package | Version | Severity | CVEs | Assessment |
|---------|---------|----------|------|------------|
| `SixLabors.ImageSharp` | 2.1.11 | HIGH | GHSA-j3p4-wp97-rph4, GHSA-j9gm-c75j-xc9q, GHSA-jjfr-hcj7-qf5w | Image loading vulnerabilities (OOB read, heap buffer overflow). Lantern does not process user-supplied images through ImageSharp; Toxy uses it for internal document rendering only with trusted intranet files. Exploitability in this context is assessed as low. **Monitor for Toxy upgrade.** |
| `SixLabors.ImageSharp` | 2.1.11 | MEDIUM | GHSA-gwg2-r3hj-4w44, GHSA-wmxv-xphr-5c9g | Same library, additional CVEs. Same assessment as above. |
| `OpenMcdf` | 3.4.0 | — | None known at this version | OLE2 compound file parsing. Now referenced directly at 3.4.0 (newer than the 3.1.0 that shipped transitively via Toxy), which resolves the earlier GHSA-5qwm-7pvp-w988 / GHSA-jxpf-xq2m-q525 advisories. |
| `System.Security.Cryptography.Xml` | 8.0.2 | HIGH | Multiple (GHSA-23rf-6693-g89p, et al.) | XML cryptography library; not called directly by Lantern code. Indirect dep via NPOI → System.Security.Cryptography.Xml path. No Lantern component signs or verifies XML digital signatures. Assessed as not exploitable in this usage. **Resolve before general release by upgrading NPOI or replacing Toxy.** |
| `System.Security.Cryptography.Pkcs` | 9.0.2 | HIGH | GHSA-846f-f764-76mx | PKCS cryptography; same indirect path. Same assessment. |
| `System.Security.Cryptography.Pkcs` | 9.0.2 | MEDIUM | GHSA-2fmv-rv2c-59x7 | Same library, additional CVE. |

**Release qualification requirement:** These HIGH findings must be resolved or re-assessed
before Phase 12 packaging. Acceptable resolutions include: upgrading Toxy to a version that
removes the vulnerable dependencies, replacing Toxy with an alternative extraction library,
or receiving documented approval from the project owner that the risk is accepted for the
specific deployment scenario.

---

## NPOI (direct, for legacy .xls)

| Property | Value |
|----------|-------|
| NuGet ID | `NPOI` |
| Version used | 2.7.5 |
| NuGet URL | https://www.nuget.org/packages/NPOI |
| Source | https://github.com/nissl-lab/npoi |
| License | Apache 2.0 |
| Redistribution | Permitted — see license notice requirement below |
| Lantern usage | `XlsExtractor` uses `HSSFWorkbook` to parse BIFF8 (.xls) workbooks with resource bounds (max 256 sheets, 50 000 rows/sheet, 1 024 cells/row) |

NPOI is a pure-managed .NET port of Apache POI. HSSF (Excel binary) is used; HWPF
(legacy Word) and HSLF (legacy PowerPoint) are **not shipped in the current NPOI
package** — that is why Lantern implements .doc and .ppt parsing directly against
the MS-DOC and MS-PPT specifications instead of relying on NPOI.

**Redistribution note:** The NPOI NuGet package is Apache 2.0, consistent with the
Apache POI upstream. The license requires preserving the Apache 2.0 NOTICE file
when redistributing the library. Lantern's release packaging includes the NPOI
license text alongside other third-party notices.

---

## OpenMcdf (direct, for CFBF/OLE2 access)

| Property | Value |
|----------|-------|
| NuGet ID | `OpenMcdf` |
| Version used | 3.4.0 |
| NuGet URL | https://www.nuget.org/packages/OpenMcdf |
| Source | https://github.com/ironfede/openmcdf |
| License | MPL 2.0 |
| Redistribution | Permitted |
| Lantern usage | `DocExtractor` and `PptExtractor` open the compound (OLE2) container to read the `WordDocument`/`1Table` and `PowerPoint Document` streams |

OpenMcdf is MPL 2.0: the library sources themselves fall under MPL, but linking to
and distributing the unmodified binary alongside Lantern's MIT-licensed code is
permitted. If you modify OpenMcdf, the modified files must be published under the
MPL. Lantern does not modify OpenMcdf.

---

## LlamaPure (NuGet)

| Property | Value |
|---|---|
| NuGet ID | `LlamaPure` |
| Version | 1.0.4 |
| License | MIT (verify against current package metadata) |
| Usage | Native llama.cpp inference for GGUF models |
| Platform | Windows x64 (native DLLs bundled) |
| Redistribution | Permitted under MIT; verify native DLL provenance |

LlamaPure wraps the llama.cpp native library. The NuGet package includes pre-built x64
Windows DLLs (`llama.dll`, `ggml*.dll`). Verify the source commit and build provenance of
the bundled native binaries before public distribution.

---

## IBM Granite Embedding 97M Multilingual R2 (GGUF model)

| Property | Value |
|---|---|
| File | `granite-embedding-97M-multilingual-r2.gguf` |
| Model family | IBM Granite |
| License | Apache 2.0 |
| Source | IBM / HuggingFace model hub |
| Dimensions | 384 |
| Redistribution | Permitted under Apache 2.0 |

The model license is Apache 2.0, independent of the inference library license.
Verify the license of the exact artifact file before distribution, as different
quantizations may have different provenance.

---

## DocumentFormat.OpenXml (direct, for DocumentGenerator)

| Property | Value |
|----------|-------|
| NuGet ID | `DocumentFormat.OpenXml` |
| Version used | 3.4.1 (via Toxy) |
| Source | https://github.com/dotnet/Open-XML-SDK |
| License | MIT |
| Redistribution | Permitted |

---

## xUnit

| Property | Value |
|----------|-------|
| NuGet ID | `xunit`, `xunit.runner.visualstudio` |
| License | Apache 2.0 |
| Usage | Test projects only — not shipped |

---

## Xunit.SkippableFact

| Property | Value |
|----------|-------|
| NuGet ID | `Xunit.SkippableFact` |
| Version | 1.5.85 |
| License | MIT |
| Usage | Test projects only — not shipped |

---

## .NET 10 Runtime / ASP.NET Core

| Property | Value |
|----------|-------|
| Runtime | .NET 10 LTS |
| License | MIT |
| Source | https://github.com/dotnet/runtime |
| Redistribution | Permitted under MIT |

---

## Summary Table

| Component | Type | License | Ships with app |
|-----------|------|---------|----------------|
| Meilisearch CE binary | External process | SSPL v1 | Yes (bundled, unmodified) |
| Meilisearch .NET client | NuGet | MIT | Yes |
| Toxy | NuGet | Apache 2.0 | Yes |
| PdfPig (via Toxy) | NuGet transitive | Apache 2.0 | Yes |
| DocumentFormat.OpenXml | NuGet transitive | MIT | Yes |
| HtmlAgilityPack | NuGet transitive | MIT | Yes |
| NPOI | NuGet transitive | Apache 2.0 | Yes |
| xUnit | NuGet | Apache 2.0 | No (tests only) |
| .NET 10 runtime | Runtime | MIT | Self-contained optional |

---

## Open Questions

1. **SSPL v1 + MIT combination**: Before public distribution, legal review of bundling
   SSPL v1 Meilisearch with MIT Lantern code is recommended.

2. **SixLabors.ImageSharp license split**: Versions >= 3.0 use the Six Labors Split License
   (commercial use requires a paid license). Version 2.1.11 (used via Toxy) is Apache 2.0.
   If Toxy is updated to depend on ImageSharp 3.x, this must be re-evaluated.

3. **Vulnerability tracking**: Several transitive packages have known CVEs.
   A dedicated security audit is recommended before v1.0 release.
