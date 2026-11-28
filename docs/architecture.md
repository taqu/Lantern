# Lantern Architecture

## Status

Phase 1 provides the Blazor search page and server-side Meilisearch search service.
Phase 2 adds the shared document model, identity and normalization, bounded indexing,
task completion and safe rebuilds. See [indexing.md](indexing.md) for the current indexing
contract, settings and concurrency decisions. The older diagrams below describe the
initial intended layout. Phase 3 now adds SQLite settings and crawler-definition persistence,
cookie authentication, and protected Blazor administration pages. See
[administration.md](administration.md) for provisioning, security, paths and migrations.
Crawler management workflows and extraction integration remain future work.

---

## System Diagram

```
Browser
   |
   v
ASP.NET Core / Blazor                  [Phase 0: minimal home page only]
   |
   +-- Search Integration              [planned — Phase 1]
   |       |
   |       v
   |   Meilisearch                     [Phase 0: feasibility verified]
   |
   +-- Crawler Management              [planned — Phase 2+]
   |       |
   |       v
   |     SQLite                        [planned — Phase 2+]
   |
   +-- Background Crawlers             [planned — Phase 2+]
           |
           +-- Local Files
           +-- Network Shares
           +-- Websites
           |
           v
      Text Extraction                  [Phase 0: library feasibility verified]
      (Toxy / PdfPig / OpenXml)
           |
           v
      Search Index (Meilisearch)
```

---

## Projects

### Lantern.Web (src/Lantern.Web)

ASP.NET Core 10 Blazor Web App with Interactive Server rendering.

**Phase 0:** Minimal home page. No search UI.

**Planned:**
- Full-text search page
- Administration dashboard
- Crawler configuration and status pages

### Lantern.Core (src/Lantern.Core)

Shared domain models and interfaces.

**Phase 0:** `SearchDocument`, `SearchResult`, `ISearchService` (interface only).

**Planned:**
- Crawler configuration models
- Job and task models

### Lantern.Infrastructure (src/Lantern.Infrastructure)

Integrations with external systems.

**Phase 0:** Meilisearch NuGet package dependency declared; no implementation yet.

**Planned:**
- Meilisearch search service implementation
- SQLite persistence

### Lantern.Crawling (src/Lantern.Crawling)

Filesystem and web crawling.

**Phase 0:** Empty project. No crawlers implemented.

**Planned:**
- Local filesystem crawler
- Network share crawler
- Web crawler (with strict target restrictions)

### Lantern.Feasibility (tools/Lantern.Feasibility)

Phase 0 command-line tool for verifying:
- Meilisearch connectivity and API (index, search, delete)
- Document text extraction via Toxy

Not part of the production application.

---

## Architectural Constraints

The following constraints apply across all phases:

1. **Meilisearch is responsible for indexing and searching.**
   The Blazor application communicates with Meilisearch only through the official .NET client (`Meilisearch` NuGet package). The application never constructs raw Meilisearch HTTP requests directly.

2. **SQLite will store configurations, crawler state, and execution metadata.**
   It is the only planned database. No separate database server is required.

3. **Blazor is the only frontend framework.**
   No React, Vue, Angular, or other JavaScript frameworks. JavaScript usage is limited to what ASP.NET Core and Blazor emit automatically.

4. **Meilisearch API keys must remain server-side.**
   The master key and search API keys are never sent to the browser. Search requests are always proxied through the ASP.NET Core backend.

5. **Meilisearch is bound to loopback (127.0.0.1) in development.**
   In production, it must not be directly exposed to intranet clients. All requests go through the Blazor application.

6. **Crawler execution will use .NET background services (`IHostedService`).**
   No external job schedulers, message queues, or distributed execution frameworks are planned for v1.0.

7. **Web crawling must have strict network target restrictions.**
   The crawler must only access explicitly configured URLs and domains. No unrestricted internet crawling.

8. **File crawling must use explicitly approved filesystem roots.**
   The crawler must only scan paths explicitly listed in the crawler configuration.

9. **No Docker, no Node.js, no Java runtime.**
   The application targets native Windows x64 deployment. The only required runtime is .NET 10.

---

## Deployment Model

```
Windows x64 Host
├── meilisearch.exe        (SSPL v1, official binary, bound to 127.0.0.1:7700)
│   └── data/              (Meilisearch index storage, excluded from git)
└── Lantern.Web            (dotnet run / Windows service — Phase 2+)
    ├── appsettings.json
    └── (no separate database server)
```

---

## Phase Roadmap Summary

| Phase | Focus |
|-------|-------|
| 0 | Project bootstrap, dependency feasibility |
| 1 | Search UI prototype — basic search page in Blazor |
| 2 | Search and indexing foundation |
| 3 | Administration, SQLite settings and administrator authentication |
| 4–12 | Extraction, crawlers, scheduling, hardening and packaging (future) |
