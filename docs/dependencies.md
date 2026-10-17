# Lantern Dependencies

This document describes the direct and notable transitive dependencies of Lantern,
including their licenses and redistribution considerations.

This is a technical assessment only. It is not a substitute for legal advice.

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

Toxy bundles PdfPig and DocumentFormat.OpenXml as dependencies.

### Toxy Transitive Dependencies (selected)

| Package | Version | License | Notes |
|---------|---------|---------|-------|
| DocumentFormat.OpenXml | 3.4.1 | MIT | DOCX/XLSX/PPTX parsing |
| PdfPig | 0.1.13 | Apache 2.0 | PDF text extraction |
| HtmlAgilityPack | 1.12.4 | MIT | HTML parsing |
| NPOI | 2.7.5 | Apache 2.0 | Legacy XLS/DOC support |
| CsvHelper | 33.1.0 | MS-PL / Apache 2.0 | CSV parsing |
| SixLabors.ImageSharp | 2.1.11 | Apache 2.0 / Six Labors Split | Image handling |
| OpenMcdf | 3.1.0 | MPL 2.0 | OLE2 container reading |

**Security notes (as of October 2026):**
NuGet vulnerability advisories flag several Toxy transitive packages:
- `SixLabors.ImageSharp` 2.1.11: known medium/high severity CVEs
- `OpenMcdf` 3.1.0: known medium severity CVEs
- `System.Security.Cryptography.Pkcs` / `Xml`: known high severity CVEs (from NPOI indirect dep)

These vulnerabilities are in transitive dependencies of Toxy 2.6.0.
They do not affect text extraction from trusted local documents in the intended usage scenario,
but should be monitored. Upgrading Toxy when a patched version is released is recommended.

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
