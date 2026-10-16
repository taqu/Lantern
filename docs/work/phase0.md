# Lantern — Phase 0: Project Bootstrap and Technical Feasibility

## Role

You are a senior .NET engineer responsible for implementing Phase 0 of Lantern, a lightweight, open-source full-text search application for Windows and intranet environments.

Implement the foundational project structure and verify critical technical dependencies.

**This is an implementation task, not merely a planning or documentation task.**

Write working code, run the available tests, investigate technical issues, and document the actual results.

Do not implement features assigned to later phases.

## 1. Project Overview

Lantern is a self-hosted full-text search platform intended to be a lightweight and easier-to-manage alternative to Fess.

The system will eventually provide:

- Full-text search powered by Meilisearch.
- A browser-based search interface using Blazor.
- A browser-based administration interface.
- Local filesystem and network-share crawling.
- Web crawling.
- Text extraction from PDF and Microsoft Office documents.
- Crawler configuration, scheduling, and execution management.
- Native Windows deployment without Docker.

The application should be straightforward to install, configure, maintain, and distribute.

### Technology decisions

- Language: C#
- Runtime: .NET 10 LTS
- Web framework: ASP.NET Core
- UI framework: Blazor Web App with Interactive Server rendering
- Search engine: Meilisearch Community Edition
- Document extraction: Toxy
- Future configuration database: SQLite
- Primary deployment platform: Windows x64
- Build system: .NET CLI and MSBuild
- Testing: xUnit

Do not introduce Node.js, Java, Docker, Elasticsearch, or external database servers.

Prefer standard .NET facilities and minimal third-party dependencies.

## 2. Phase 0 Objectives

Phase 0 must accomplish the following:

1. Establish a clean, buildable .NET solution.
2. Create a minimal working Blazor application.
3. Verify native Windows compatibility with Meilisearch.
4. Verify .NET-to-Meilisearch communication.
5. Evaluate Toxy with real sample documents.
6. Determine whether PDF and PPTX extraction are feasible.
7. Investigate the redistribution requirements of important dependencies.
8. Document architecture and development procedures.

The result should be a stable starting point for Phase 1, which will implement the first search UI prototype.

## 3. Repository Initialization

Inspect the existing repository before making changes.

If an existing solution or project structure is present, preserve useful conventions and avoid unnecessary restructuring.

Otherwise, initialize the following structure:

```text
Lantern/
├── src/
│   ├── Lantern.Web/
│   ├── Lantern.Core/
│   ├── Lantern.Infrastructure/
│   └── Lantern.Crawling/
├── tests/
│   ├── Lantern.Core.Tests/
│   └── Lantern.IntegrationTests/
├── tools/
│   └── Lantern.Feasibility/
├── samples/
│   └── documents/
├── docs/
│   ├── architecture.md
│   ├── dependencies.md
│   └── feasibility.md
├── scripts/
├── .editorconfig
├── .gitignore
├── Directory.Build.props
├── Lantern.sln
├── README.md
└── LICENSE
```

Use a conventional .NET solution structure.

Do not add additional projects unless they serve a concrete purpose.

### Project responsibilities

**Lantern.Web**

ASP.NET Core application and Blazor components.

For Phase 0, it only needs to display a basic application page and start successfully.

**Lantern.Core**

Shared domain models and interfaces.

Keep this project minimal. Do not create speculative domain abstractions.

**Lantern.Infrastructure**

Future integration with Meilisearch, SQLite, and document extraction.

Only implement what is needed for feasibility testing.

**Lantern.Crawling**

Future filesystem and website crawling implementation.

Create the project if useful, but do not implement crawlers.

**Lantern.Feasibility**

A small command-line application for independently exercising Meilisearch connectivity and document extraction.

Keep feasibility experiments separate from production application code.

## 4. Build Configuration

Configure the solution with:

- .NET 10 target framework.
- Nullable reference types enabled.
- Implicit usings enabled.
- Consistent language and compiler settings.
- Deterministic builds where applicable.
- Standard .NET formatting conventions.

Use `Directory.Build.props` for appropriate shared settings.

Use NuGet package versions that are compatible with .NET 10.

Avoid unnecessary warning suppression.

Do not enable warnings-as-errors globally unless the initial solution can build cleanly and the behavior is documented.

### Required verification

Run:

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

All commands must succeed in a correctly provisioned development environment.

If commands cannot be executed, report the exact environmental limitation rather than claiming success.

## 5. Minimal Blazor Application

Create a minimal ASP.NET Core Blazor Web App using Interactive Server rendering.

Requirements:

- Application name: Lantern.
- A working home page.
- Basic navigation.
- A simple page indicating that the application is running.
- Default ASP.NET Core dependency injection.
- Standard configuration through `appsettings.json`.
- Standard Microsoft.Extensions.Logging integration.

Keep the visual design minimal and conventional.

Do not introduce third-party UI component frameworks.

Do not implement search controls, result lists, administration dashboards, or crawler configuration pages yet.

### Acceptance criteria

- The application compiles.
- Kestrel starts successfully.
- The home page loads in a browser when the environment supports browser testing.
- The application can be started with `dotnet run`.
- No JavaScript build pipeline is required.

## 6. Meilisearch Native Windows Feasibility

Investigate the current official Meilisearch Community Edition release.

Verify:

1. Availability of a native Windows x64 executable.
2. Whether it runs without Docker.
3. Runtime prerequisites.
4. Data storage and persistence behavior.
5. Configuration options.
6. API key and authentication behavior.
7. Community Edition license and redistribution conditions.

Use official project documentation and release artifacts whenever possible.

Do not assume that Windows support or redistribution conditions are valid without checking the selected release.

### Native process test

If running on Windows x64, download or otherwise obtain an official Meilisearch Community Edition executable from a trustworthy source.

Record:

- Exact Meilisearch version.
- Release URL.
- File checksum, if published or calculated.
- License identifier.
- Required runtime dependencies.
- Startup command.
- Observed startup result.

Run Meilisearch as a normal Windows process.

Use a temporary data directory.

Bind the test instance to loopback only.

Enable authentication using a development-only master key supplied through an environment variable or another noncommitted local configuration mechanism.

Do not hardcode production credentials.

Do not expose the test instance to the network.

Verify that the process starts, accepts authenticated HTTP requests, and can be stopped cleanly.

### Cross-platform execution

If the Coding Agent is running on Linux or macOS, it may perform compatible API integration tests using an official native binary for that environment.

However, such tests do not establish Windows compatibility.

In that case:

- Verify Windows artifact availability from official release information.
- Document the exact untested Windows requirements.
- Provide a repeatable PowerShell verification script.
- Mark actual Windows execution as unverified.

Never claim that a Windows-native test passed when it was not performed on Windows.

## 7. Meilisearch .NET Integration Test

Create a small .NET feasibility test using the official Meilisearch .NET client, if suitable and compatible.

The test must:

1. Connect to a running Meilisearch instance.
2. Check service availability.
3. Create or reuse a temporary test index.
4. Insert at least three sample documents.
5. Wait for asynchronous indexing tasks to finish.
6. Execute a full-text search.
7. Verify that expected documents are returned.
8. Delete the temporary test index.
9. Report failures clearly.

Use test data such as:

```json
[
  {
    "id": 1,
    "title": "Lantern Introduction",
    "content": "Lantern is a lightweight full-text search platform."
  },
  {
    "id": 2,
    "title": "Document Extraction",
    "content": "PDF and Office documents can be indexed."
  },
  {
    "id": 3,
    "title": "Web Crawling",
    "content": "Website content can be discovered and searched."
  }
]
```

Keep this test independent from the Blazor user interface.

Do not implement the production search service or document indexing pipeline yet.

### Acceptance criteria

- Documents can be indexed.
- Search returns expected results.
- Asynchronous Meilisearch task completion is correctly handled.
- Errors do not disappear silently.
- Temporary test data can be cleaned up.
- Integration testing does not require Docker.

## 8. Toxy Document Extraction Evaluation

Install and evaluate the current stable Toxy NuGet package.

Use Toxy as the preferred extraction framework.

Determine what formats are actually supported by the selected version.

Do not rely solely on package descriptions.

### Required format matrix

Evaluate these document formats:

| Format | Requirement |
|---|---|
| TXT | Required |
| HTML | Required |
| PDF | Required |
| DOCX | Required |
| XLSX | Required |
| PPTX | Required |
| CSV | Evaluation only |

Create a small command-line extraction utility under `tools/Lantern.Feasibility`.

The utility should:

- Accept a document path.
- Identify or select an appropriate parser.
- Extract text using Toxy.
- Display the extracted text.
- Report detected format information.
- Report exceptions and unsupported formats clearly.
- Avoid writing extracted data into the search index.

### Test fixtures

Create or include a small set of legally redistributable sample documents.

Each fixture should contain known text that can be checked programmatically.

Use both English and Japanese text where practical.

Include representative cases such as:

- Plain text with multiple lines.
- HTML with markup and noncontent elements.
- PDF containing selectable text.
- DOCX containing paragraphs and a simple table.
- XLSX containing multiple populated cells.
- PPTX containing text on multiple slides.

Use harmless, locally generated files when possible.

Avoid including copyrighted or confidential documents.

### PPTX compatibility investigation

PPTX support is a critical requirement.

Determine whether Toxy can correctly extract PPTX text in the selected version.

Check:

- Basic slide text.
- Text from multiple slides.
- Text inside shapes.
- Text in tables, where practical.
- Japanese text.
- Failure behavior on unsupported content.

If Toxy does not support PPTX or produces incomplete results, investigate a lightweight, .NET-compatible alternative.

Prefer an Open XML-based approach if appropriate.

Document the recommended fallback implementation, compatibility, limitations, and license.

Do not implement a full alternative extraction framework in Phase 0.

### PDF compatibility investigation

Verify that Toxy can extract text from ordinary text-based PDFs.

Explicitly distinguish between:

- Text-based PDF documents.
- Scanned image-only PDF documents.
- Encrypted or password-protected PDFs.

OCR is outside the scope of Lantern v1.0.

Do not introduce OCR dependencies.

### Required report

Create `docs/feasibility.md` with a compatibility table containing:

- File format.
- Toxy support.
- Actual extraction result.
- Text correctness.
- Japanese text support.
- Known limitations.
- Recommended fallback, if necessary.

Use the following statuses:

- PASS
- PARTIAL
- FAIL
- NOT TESTED

Do not classify an untested format as supported.

## 9. Licensing and Redistribution Investigation

Review the licenses of:

- Meilisearch Community Edition.
- Toxy.
- Meilisearch .NET client.
- Any additional libraries introduced.
- Relevant transitive dependencies.

The intended distribution model is:

- Open-source Lantern application.
- Bundled Meilisearch Community Edition executable.
- Self-contained .NET application.
- No mandatory Docker installation.
- No mandatory cloud service.

### Requirements

Determine:

1. Applicable license identifiers.
2. Whether redistribution is permitted.
3. Whether license and copyright notices must be included.
4. Whether additional conditions apply to bundled binaries.
5. Whether dependencies introduce conflicting requirements.
6. Whether separate enterprise/commercial editions must be excluded.

Create `docs/dependencies.md`.

Include official source URLs and selected versions.

Do not simply claim that all dependencies are MIT-licensed.

If there are unresolved licensing questions, identify them explicitly.

Do not introduce a license-incompatible package without documenting the issue.

This is a technical redistribution assessment, not a substitute for legal advice.

## 10. Initial Architecture Documentation

Create `docs/architecture.md`.

Describe the intended architecture:

```text
Browser
   |
   v
ASP.NET Core / Blazor
   |
   +-- Search Integration
   |       |
   |       v
   |   Meilisearch
   |
   +-- Crawler Management
   |       |
   |       v
   |     SQLite
   |
   +-- Background Crawlers
           |
           +-- Local Files
           +-- Network Shares
           +-- Websites
           |
           v
      Text Extraction
           |
           v
      Search Index
```

Clarify that most components shown are planned rather than implemented in Phase 0.

Document the following architectural constraints:

- Meilisearch is responsible for indexing and searching.
- SQLite will store configurations, crawler state, and execution metadata.
- Blazor will be the only frontend framework.
- Meilisearch API keys must remain server-side.
- Meilisearch should not be directly exposed to intranet browsers.
- Crawler execution will eventually use .NET background services.
- Web crawling must have strict network target restrictions.
- File crawling must use explicitly approved filesystem roots.
- No distributed execution framework is planned for v1.0.

Avoid detailed interfaces for functionality that has not been implemented.

## 11. Development Scripts

Provide simple scripts for the development environment.

Preferred examples:

```text
scripts/
├── start-meilisearch.ps1
├── stop-meilisearch.ps1
└── run-feasibility.ps1
```

Scripts should be safe and reproducible.

Requirements:

- Do not require administrator privileges unless necessary.
- Do not install permanent Windows services.
- Do not modify global environment variables.
- Do not terminate unrelated Meilisearch processes.
- Do not assume fixed absolute installation paths.
- Avoid storing credentials in tracked source files.
- Use temporary or configurable data directories.

Only manage processes explicitly started by the scripts.

If a dependency is unavailable, print an actionable message.

## 12. README

Create a concise but useful `README.md`.

Include:

- Project overview.
- Main technical components.
- Prerequisites.
- Build instructions.
- How to launch the Blazor application.
- How to run Meilisearch for development.
- How to execute feasibility tests.
- Phase 0 limitations.
- Planned Phase 1 functionality.

Clearly distinguish current implementation from planned features.

Do not advertise crawling or full-text search UI functionality as implemented.

## 13. Non-Goals

Do not implement any of the following during Phase 0:

- Production search page.
- Search result UI.
- Administration dashboard.
- Authentication or user management.
- SQLite persistence layer.
- Database migrations.
- File crawler.
- Web crawler.
- Crawler scheduling.
- Background job management.
- Production indexing pipeline.
- Search filtering or ranking customization.
- Windows service integration.
- Release installers.
- Automatic Meilisearch process supervision.
- CI/CD workflows beyond what is strictly necessary for local build validation.

Avoid adding placeholder implementations for these features.

The objective is to verify feasibility and establish a minimal foundation, not to build the complete application.

## 14. Engineering Quality Requirements

Follow these rules:

1. Favor clarity over clever abstractions.
2. Keep dependencies minimal.
3. Avoid unnecessary architectural patterns.
4. Use idiomatic modern C#.
5. Enable nullable reference types.
6. Handle recoverable external-process and HTTP errors.
7. Avoid broad exception swallowing.
8. Do not log credentials or sensitive document contents unnecessarily.
9. Do not commit downloaded executables, generated data, or temporary extraction output.
10. Keep test fixtures small and deterministic.
11. Make every verification reproducible.
12. Do not fabricate successful tests or compatibility results.

## 15. Acceptance Criteria

Phase 0 is complete when:

- [ ] A valid .NET 10 solution exists.
- [ ] All required projects compile.
- [ ] The minimal Blazor application starts successfully.
- [ ] Meilisearch native Windows availability has been verified.
- [ ] Native Windows execution has been tested, or explicitly marked unverified if a Windows environment is unavailable.
- [ ] A .NET client can index and search sample documents.
- [ ] Toxy has been evaluated against the required document formats.
- [ ] PPTX compatibility and any necessary fallback have been documented.
- [ ] PDF limitations have been documented.
- [ ] Licensing and redistribution requirements have been investigated.
- [ ] Documentation contains reproducible setup and test procedures.
- [ ] Release build and available tests pass.
- [ ] No Phase 1 or later features have been implemented prematurely.

## 16. Completion Report

At the end of the implementation, provide a concise report containing:

### Implemented

Summarize the actual code and repository changes.

### Build and Test Results

List the commands executed and whether they passed.

### Meilisearch Verification

Report:

- Version.
- Host operating system.
- Native executable startup result.
- API connectivity result.
- Indexing and searching result.
- Any Windows-specific unresolved issues.

### Toxy Compatibility Matrix

Provide the tested formats and actual outcomes.

### Dependency and License Findings

Summarize the selected dependencies, licenses, and unresolved questions.

### Known Limitations

Identify anything incomplete, unavailable, or unverified.

### Phase 1 Readiness

State whether Phase 1 can proceed.

If a blocking issue exists, explain the smallest reasonable corrective action.

## 17. Final Instructions

Implement Phase 0 now.

Inspect the existing repository first.

Make the necessary changes directly.

Run builds and tests whenever the environment permits.

Investigate and document real compatibility rather than assuming it.

Keep the implementation small and maintainable.

Do not expand the scope into Phase 1.

Do not stop after generating a plan. Produce the working Phase 0 foundation and report the actual results.
