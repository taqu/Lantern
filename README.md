# Lantern

A lightweight, self-hosted full-text search platform for Windows and intranet environments.

**Current status: Phase 0 — project bootstrap and dependency feasibility.**

---

## Overview

Lantern is intended to be an easier-to-manage alternative to Fess for Windows-based intranet environments.

**Planned features (not yet implemented):**

- Full-text search powered by Meilisearch Community Edition
- Browser-based search interface built with ASP.NET Core Blazor
- Local filesystem and network-share crawling
- PDF and Microsoft Office document text extraction
- Browser-based administration interface
- Native Windows deployment without Docker

---

## Technology Stack

| Component | Technology |
|-----------|-----------|
| Runtime | .NET 10 LTS |
| Web framework | ASP.NET Core 10 |
| UI | Blazor Web App (Interactive Server) |
| Search engine | Meilisearch Community Edition |
| Document extraction | Toxy 2.6.0 |
| Testing | xUnit |
| Target OS | Windows x64 |

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Meilisearch Windows binary](https://github.com/meilisearch/meilisearch/releases/latest) (for search and integration tests)

No Docker, Node.js, Java, or database server is required.

---

## Repository Structure

```
Lantern/
├── src/
│   ├── Lantern.Web/              Blazor Web App (minimal home page)
│   ├── Lantern.Core/             Shared domain models and interfaces
│   ├── Lantern.Infrastructure/   Future: Meilisearch integration
│   └── Lantern.Crawling/         Future: filesystem and web crawlers
├── tests/
│   ├── Lantern.Core.Tests/       Unit tests (always run)
│   └── Lantern.IntegrationTests/ Integration tests (require Meilisearch)
├── tools/
│   └── Lantern.Feasibility/      Phase 0 feasibility tool
├── samples/documents/            Test fixtures for extraction testing
├── docs/                         Architecture, dependencies, feasibility
├── scripts/                      Development PowerShell scripts
├── Directory.Build.props         Shared MSBuild settings
└── Lantern.sln
```

---

## Build

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

Unit tests pass without any external dependencies.
Integration tests are skipped unless `MEILI_MASTER_KEY` is set.

---

## Run the Web Application

```powershell
dotnet run --project src/Lantern.Web
```

Then open: http://localhost:5000

The home page shows "Lantern is running." Search functionality is not yet available.

---

## Run Meilisearch for Development

1. Download `meilisearch-windows-amd64.exe` from https://github.com/meilisearch/meilisearch/releases
2. Place it at `tools/meilisearch/meilisearch.exe`
3. Set your master key:
   ```powershell
   $env:MEILI_MASTER_KEY = "your-dev-key-here"
   ```
4. Start Meilisearch:
   ```powershell
   .\scripts\start-meilisearch.ps1
   ```
5. Stop Meilisearch:
   ```powershell
   .\scripts\stop-meilisearch.ps1
   ```

Meilisearch binds to `127.0.0.1:7700` only. Never expose it directly to the network.

---

## Run Feasibility Tests

```powershell
# Set master key (required for Meilisearch test)
$env:MEILI_MASTER_KEY = "your-dev-key-here"

# Start Meilisearch first
.\scripts\start-meilisearch.ps1

# Run all feasibility tests (generates samples, tests Meilisearch, tests extraction)
.\scripts\run-feasibility.ps1

# Or run specific tests:
dotnet run --project tools/Lantern.Feasibility -- --meili             # Meilisearch only
dotnet run --project tools/Lantern.Feasibility -- --generate-samples  # Generate sample docs
dotnet run --project tools/Lantern.Feasibility -- --extract samples/documents/sample.txt
```

---

## Run Integration Tests

```powershell
$env:MEILI_MASTER_KEY = "your-dev-key-here"
dotnet test tests/Lantern.IntegrationTests -c Release
```

Tests are skipped (not failed) if `MEILI_MASTER_KEY` is not set.

---

## Phase 0 Limitations

The following are **not** implemented in Phase 0:

- Search UI or result display
- Document indexing pipeline
- Filesystem or web crawler
- Administration interface
- SQLite configuration database
- User authentication
- Windows service integration
- Scheduler or background job management

---

## Planned Phase 1

Phase 1 will implement the first search UI prototype:

- A working search page in Blazor
- Manual document indexing via the administration interface
- Basic result display with title, snippet, and source path
- Meilisearch service abstraction in `Lantern.Infrastructure`

---

## License

Copyright 2026 taqu. MIT License. See [LICENSE](LICENSE).

Meilisearch is licensed under SSPL v1 and is distributed separately.
See [docs/dependencies.md](docs/dependencies.md) for details.
