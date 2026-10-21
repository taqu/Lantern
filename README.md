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
- Document extraction: PDF, DOCX, XLSX, PPTX, HTML, plain text
- Windows Service support
- Offline operation — no internet required after installation

---

## Quick Start

1. Extract `Lantern-<version>-win-x64.zip` to `C:\Lantern\`
2. Run as Administrator:
   ```powershell
   .\scripts\Initialize-Lantern.ps1
   ```
3. Start Lantern:
   ```powershell
   .\scripts\Start-Lantern.ps1
   ```
4. Open `http://127.0.0.1:5000` in your browser.

See [`release/docs/INSTALL.md`](release/docs/INSTALL.md) for full instructions.

---

## Documentation

| Document | Description |
|----------|-------------|
| [INSTALL.md](release/docs/INSTALL.md) | Installation and first-run |
| [CONFIGURATION.md](release/docs/CONFIGURATION.md) | Configuration reference |
| [WINDOWS-SERVICE.md](release/docs/WINDOWS-SERVICE.md) | Windows Service setup |
| [UPGRADE.md](release/docs/UPGRADE.md) | Upgrade procedure |
| [BACKUP-RESTORE.md](release/docs/BACKUP-RESTORE.md) | Backup and restore |
| [TROUBLESHOOTING.md](release/docs/TROUBLESHOOTING.md) | Troubleshooting |
| [THIRD-PARTY-NOTICES.md](release/docs/THIRD-PARTY-NOTICES.md) | Third-party licenses |

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

See [THIRD-PARTY-NOTICES.md](release/docs/THIRD-PARTY-NOTICES.md) for full license texts.
