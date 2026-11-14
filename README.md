# Lantern

Self-hosted full-text and hybrid search platform for Windows.

Lantern crawls local files and web pages, extracts their content, and makes them
searchable through a browser-based interface. It supports keyword search and optional
local AI-powered semantic search — entirely offline, with no cloud dependencies.

---

## Features

- Incremental file and web crawlers
- Full-text keyword search (Meilisearch)
- Hybrid search — keyword + local semantic embeddings (Granite 97M)
- Administrator-managed search categories
- Document extraction: PDF, DOCX, XLSX, PPTX, legacy DOC/XLS/PPT (Office 97–2003), HTML, plain text
- Windows Service support
- Offline operation — no internet required after installation

---

## Quick Start

1. Extract `Lantern-<version>-win-x64.zip` to a directory of your choice.

2. Open PowerShell in the extracted directory.

3. Start Lantern (no elevation required):

   ```powershell
   .\start-lantern.ps1
   ```

4. On first launch, create the administrator account when prompted.

5. Open the URL displayed by Lantern in your browser.

See [`docs/INSTALL.md`](docs/INSTALL.md) for Windows Service setup and advanced configuration.

---

## Documentation

| Document | Description |
|----------|-------------|
| [INSTALL.md](docs/INSTALL.md) | Installation and first-run |
| [development.md](docs/development.md) | Developer guide |
| [administration.md](docs/administration.md) | Administrator reference |
| [operations.md](docs/operations.md) | Operations reference |
| [backup-recovery.md](docs/backup-recovery.md) | Backup and restore |
| [security.md](docs/security.md) | Security model |
| [dependencies.md](docs/dependencies.md) | Third-party components and licenses |

---

## System Requirements

- Windows 10 x64 (build 1903+) or Windows 11 x64
- 2 GB RAM minimum (4 GB recommended with embedding)
- 2 GB disk (plus ~200 MB for the Granite model if using hybrid search)
- No Docker, Java, Node.js, or separately installed .NET runtime required

---

## Components

| Component | Version | License |
|-----------|---------|---------|
| .NET | 10 (self-contained) | MIT |
| Meilisearch | 1.55.0 | SSPL v1 |
| LlamaPure | 1.0.4 | MIT |
| IBM Granite Embedding 97M | R2 | Apache 2.0 |

See [dependencies.md](release/docs/dependencies.md) for full component versions and license information.
