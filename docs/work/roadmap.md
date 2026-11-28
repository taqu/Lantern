Lantern は、Meilisearch を検索エンジンとして活用し、.NET でクローリング・文書抽出・管理・Web UI を実装する構成がよいと思います。
Fess の代替を目指す場合も、Fess の機能をすべて再現するより、Windows 環境での簡単な導入、直感的な管理画面、安定したクローリング、軽量な運用を優先したほうが、Lantern の特色を明確にできます。
今回は、Coding Agent が各フェーズを順番に実装できるよう、Phase 0〜12 の英語ロードマップにまとめます。検索 UI のプロトタイプから始め、検索機能、管理 UI、ファイル・Web クローラ、運用機能、CI、Windows 向けリリースまで進める構成です。
事前に確認した技術的なポイント
項目	確認結果・推奨
.NET	.NET 10 LTS。2028年11月までサポート  Microsoft Learn
フロントエンド	Blazor Web App / Interactive Server
検索エンジン	Meilisearch Community Edition
Windows 対応	公式 Windows x64 実行ファイルあり。Docker 不要  GitHub
Meilisearch ライセンス	Community Edition は MIT。Enterprise Edition は別ライセンス  GitHub +1
文書抽出	Toxy 2.6.0、Apache-2.0  GitHub +1
PDF / PPTX	Toxy を優先するが、PPTX の対応状況と実際の抽出品質はテストで確認
設定・ジョブ状態管理	SQLite
配布	Windows x64、自己完結型 .NET アプリ + Meilisearch
特に注意したいのは Toxy の PPTX 対応と Meilisearch のライセンスです。Toxy は PDF などを明示的にサポートしていますが、PPTX は現在の README からだけでは保証できません。Phase 0 で実ファイルを使って検証し、必要なら PPTX 専用の抽出実装を追加するのが安全です。
また Meilisearch は、Community Edition のバイナリをライセンス表記付きで同梱する方針が現実的です。ただし、依存ライブラリを含む配布物全体のライセンス確認はリリース前に必要です。
推奨アーキテクチャ
Web Browser
Search UI / Administration UI

HTTP / HTTPS

Lantern — ASP.NET Core / Blazor Server
Search Service


Admin Service


Crawler Scheduler


Document Extraction


Security / Auth


Job Management





Meilisearch
Full-text index / Search API


SQLite
Configuration / Jobs / Crawl state




Local / Network Files
NTFS / SMB / UNC


Websites
HTTP / HTML / PDF




ASP.NET Core を唯一の外部公開エンドポイントにして、Meilisearch と SQLite はアプリ内部のサービスとして扱います。管理画面も検索画面も同じ Blazor アプリに収め、別途フロントエンド用の Node.js 環境を必要としない構成です。
セキュリティは、アプリへの認証・管理権限・検索パラメータの検証・クローラの URL 制限を中心にします。元ファイルごとの Windows ACL を検索結果に完全反映する機能は初期スコープ外とし、最初は管理者が承認した検索対象を、認証済み利用者が共通に検索できるモデルを想定します。

Lantern — Development Roadmap
Agent-oriented implementation plan · Version 0.1 · October 2026
1. Project Vision
Lantern is a lightweight, self-hosted full-text search platform for Windows and intranet environments.
It combines Meilisearch with a .NET-based crawler management system, document extraction pipeline, and browser-based user interface.
Lantern aims to provide a simpler, more approachable alternative to Fess without reproducing the complexity of a traditional enterprise search platform.
Primary goals
- Provide a clean and fast full-text search experience.
- Index local files, network folders, and websites.
- Extract text and metadata from PDF, Office documents, HTML, and plain-text files.
- Create, configure, schedule, and monitor crawlers entirely through a web browser.
- Run on Windows without Docker, Java, Node.js, or external database servers.
- Distribute Lantern together with Meilisearch Community Edition.
- Provide a straightforward setup and administration experience.
Non-goals for v1.0
- Distributed crawling or clustered deployments.
- AI search, embeddings, RAG, or chat interfaces.
- OCR for scanned documents.
- Per-document Windows ACL synchronization.
- Browser automation for JavaScript-heavy websites.
- Complex multi-tenant environments.
- Plugin marketplaces or custom scripting engines.
- Cloud-based services or mandatory internet connectivity.
- Compatibility with unsupported Windows releases.
Engineering principles
1. Prefer simple, maintainable .NET implementations.
2. Avoid unnecessary architectural layers and dependencies.
3. Keep the user interface conventional and understandable.
4. Use Meilisearch for search, not for crawler state or configuration.
5. Keep all credentials and Meilisearch API keys on the server.
6. Implement vertical slices that can be demonstrated and tested independently.
7. Do not introduce microservices or a distributed job framework.
8. Ensure that the production application works offline after installation.
2. Technical Architecture
Component	Technology
Runtime	.NET 10 LTS
Language	C#
UI	Blazor Web App, Interactive Server
Web Server	ASP.NET Core / Kestrel
Search Backend	Meilisearch Community Edition
Search Client	Official Meilisearch .NET SDK
Metadata Database	SQLite with EF Core
Document Extraction	Toxy, with format-specific fallback adapters
HTML Processing	Lightweight HTML parser
File Crawling	System.IO
Web Crawling	HttpClient
Job Execution	BackgroundService
Scheduling	Internal persistent scheduler
Logging	Microsoft.Extensions.Logging
Tests	xUnit
CI	GitHub Actions
Distribution	Windows x64 self-contained release
Proposed repository structure
Lantern/
├── src/
│   ├── Lantern.Web/
│   │   ├── Components/
│   │   │   ├── Pages/
│   │   │   ├── Search/
│   │   │   └── Admin/
│   │   └── wwwroot/
│   │
│   ├── Lantern.Core/
│   │   ├── Models/
│   │   ├── Interfaces/
│   │   └── Services/
│   │
│   ├── Lantern.Infrastructure/
│   │   ├── Persistence/
│   │   ├── Search/
│   │   └── Extraction/
│   │
│   ├── Lantern.Crawling/
│   │   ├── Files/
│   │   ├── Web/
│   │   └── Scheduling/
│   │
│   └── Lantern.Host/
│       └── Windows/
│
├── tests/
│   ├── Lantern.Core.Tests/
│   ├── Lantern.Crawling.Tests/
│   └── Lantern.IntegrationTests/
│
├── samples/
│   └── documents/
│
├── docs/
├── scripts/
├── .github/workflows/
├── Directory.Build.props
├── Lantern.sln
├── README.md
└── LICENSE


The project structure is a starting point, not a strict requirement. Avoid creating empty abstractions or assemblies before they become useful.
3. Roadmap Overview
Implementation sequence
0

Technical Feasibility
Dependencies, licenses, Windows binaries


1

Search UI Prototype
Search an existing Meilisearch index

Milestone

2

Search Foundation
Document model, indexing and search integration


3

Administration Foundation
SQLite settings, authentication, dashboard


4

Document Extraction
Toxy and format coverage


5

Local File Crawler
Folder discovery and incremental indexing

Milestone

6

Crawler Configuration UI
Browser-based crawler creation and editing


7

Crawler Execution Management
Jobs, scheduling, progress and logs


8

Web Crawler
Website discovery and content extraction

Milestone

9

Search Experience
Filtering, highlights, source navigation


10

Security and Reliability
Crawler boundaries and failure recovery


11

CI and Quality Assurance
Automated builds, tests and checks


12

Windows Release
Packaging, documentation and final validation

Milestone


Each phase should be implemented as one focused Coding Agent assignment, with smaller commits and tests as necessary. A phase is complete only when its acceptance criteria pass.

Phase 0 — Technical Feasibility and Project Bootstrap
Objective: Validate all critical dependencies before implementing application features.
Tasks
1. Create the .NET 10 solution and basic project layout.
2. Configure nullable reference types, implicit usings, and consistent build settings.
3. Add a basic ASP.NET Core Blazor application.
4. Verify that the official Meilisearch Community Edition binary runs natively on Windows x64.
5. Establish a minimal .NET connection to the Meilisearch HTTP API.
6. Evaluate Toxy against representative PDF, DOCX, XLSX, PPTX, HTML, and TXT files.
7. Identify unsupported formats and required fallback extraction libraries.
8. Review Meilisearch Community Edition, Toxy, and transitive dependency licenses.
9. Document the initial architecture and development commands.
Deliverables
- Compiling .NET solution.
- Running minimal Blazor application.
- Meilisearch connectivity test.
- Document extraction compatibility matrix.
- Initial third-party license inventory.
- docs/architecture.md
- docs/dependencies.md
Acceptance criteria
- dotnet build succeeds.
- The Blazor application starts on Windows.
- Meilisearch can be started without Docker.
- A .NET test application successfully indexes and searches sample data.
- PDF and PPTX extraction capabilities are documented using actual test results.
- No unresolved license blocker prevents the planned distribution.
Scope restriction: Do not implement the search interface or crawler framework in this phase.

Phase 1 — Search UI Prototype
Objective: Build the first usable search interface against an existing Meilisearch index.
Tasks
1. Implement a conventional search page using Blazor.
2. Add a search text field and submit action.
3. Connect to Meilisearch through a server-side search service.
4. Display result titles, URLs or paths, excerpts, and document types.
5. Implement simple pagination.
6. Add loading, no-results, and connection-error states.
7. Populate a sample index using a small development seed utility.
8. Support navigating to the original document URL.
9. Keep the interface usable on standard desktop browsers.
UI requirements
- Search field at the top.
- Results displayed as a traditional vertical list.
- Clearly visible titles.
- Short snippets underneath each title.
- Source path or URL.
- Document type indicator.
- No animated layouts, dashboards, or unnecessary visual components.
Deliverables
- /search route.
- Search service interface and implementation.
- Sample dataset and development seeding command.
- Minimal search UI component tests.
Acceptance criteria
- A user can enter a query and receive actual Meilisearch results.
- Empty queries and missing results are handled gracefully.
- Search requests do not expose Meilisearch credentials to the browser.
- Meilisearch downtime produces a useful error message rather than an unhandled exception.
Scope restriction: Do not build crawler configuration or administration pages yet.

Phase 2 — Search and Indexing Foundation
Objective: Establish the document representation and indexing operations shared by all crawlers.
Tasks
1. Define a normalized searchable document model.
2. Introduce a deterministic document ID based on source identity.
3. Support file paths and web URLs as distinct source types.
4. Implement document upsert and deletion operations.
5. Configure Meilisearch searchable, filterable, and sortable attributes.
6. Normalize source metadata and extracted text.
7. Support bounded document ingestion batches.
8. Handle asynchronous Meilisearch task completion and failures.
9. Implement a basic full-index rebuild operation.
10. Add integration tests using a real Meilisearch instance.
Initial document schema
{
  "id": "stable-document-id",
  "sourceId": "source-identifier",
  "sourceType": "file",
  "title": "Example Document",
  "uri": "file:///C:/Documents/example.pdf",
  "path": "C:\\Documents\\example.pdf",
  "content": "Extracted full text",
  "extension": ".pdf",
  "mimeType": "application/pdf",
  "size": 12345,
  "modifiedAt": "2026-10-01T10:00:00Z",
  "indexedAt": "2026-10-01T10:05:00Z"
}


Acceptance criteria
- Documents can be added, updated, and deleted.
- Re-indexing the same document does not create duplicates.
- Search results contain normalized source metadata.
- Indexing failures are reported reliably.
- Integration tests cover index creation, indexing, search, and deletion.

Phase 3 — Administration Foundation
Objective: Introduce persistent application configuration and a basic administration interface.
Tasks
1. Add SQLite persistence using EF Core.
2. Define application settings and crawler configuration entities.
3. Implement initial database migrations.
4. Create an administration layout and navigation menu.
5. Implement Meilisearch connection and health status.
6. Display indexed document count and basic system information.
7. Add configuration screens for search connection and application settings.
8. Implement an initial administrator authentication mechanism.
9. Separate public search routes from administrator-only routes.
10. Prevent secrets from being displayed or logged.
Administration navigation
Administration
├── Overview
├── Crawlers
├── Jobs
├── Search Index
├── Settings
└── System Status


Acceptance criteria
- Configuration survives an application restart.
- Administrator-only pages cannot be accessed without authorization.
- Meilisearch health status is visible.
- SQLite schema upgrades use tracked migrations.
- No master API key is delivered to the browser.
Scope restriction: Do not build a complete user-management system. Implement one secure administrator identity and a clear path for adding intranet authentication later.

Phase 4 — Document Extraction Pipeline
Objective: Extract consistent searchable content from supported document formats.
Tasks
1. Introduce an IDocumentExtractor abstraction.
2. Integrate Toxy as the primary text extraction library.
3. Implement a format-selection mechanism.
4. Add support for plain-text and HTML extraction.
5. Implement PDF, DOCX, XLSX, and PPTX extraction using verified adapters.
6. Preserve available document titles and metadata.
7. Normalize line breaks and whitespace.
8. Handle malformed, encrypted, or unsupported documents.
9. Enforce file-size and extraction-time limits.
10. Create a document extraction regression-test corpus.
Target formats
Format	Priority
TXT / Markdown	Required
HTML / HTM	Required
PDF	Required
DOCX	Required
XLSX	Required
PPTX	Required
CSV	Secondary
Legacy DOC / XLS / PPT	Deferred
PPTX extraction may use a dedicated Open XML implementation if Toxy does not provide acceptable results.
Acceptance criteria
- All required formats extract searchable text from representative fixtures.
- Japanese and English text is preserved correctly.
- A malformed document does not crash the application.
- Extraction failures produce actionable diagnostics.
- Extracted content is independent of the user interface.

Phase 5 — Local File Crawler
Objective: Index directories and network shares with reliable incremental synchronization.
Tasks
1. Implement a crawler for a configured root directory.
2. Recursively enumerate files.
3. Support configurable allowed and excluded extensions.
4. Support excluded directories and file-size limits.
5. Pass supported files to the extraction pipeline.
6. Index extracted documents into Meilisearch.
7. Persist file fingerprints and crawl state in SQLite.
8. Skip unchanged files during subsequent crawls.
9. Update changed documents and remove deleted documents.
10. Handle inaccessible paths, locked files, and permission failures.
11. Support cancellation and configurable bounded concurrency.
12. Use polling scans initially rather than relying on file-system watchers.
Acceptance criteria
- A local directory can be indexed successfully.
- A UNC network share can be indexed with suitable service-account permissions.
- Modified files are updated on the next crawl.
- Deleted files are removed from the index after a successful reconciliation.
- A canceled or interrupted scan does not incorrectly delete valid documents.
- Repeated scans do not accumulate duplicate documents.
Milestone: Lantern can now search real local documents.

Phase 6 — Crawler Configuration UI
Objective: Allow administrators to create and edit crawler definitions through the browser.
Tasks
1. Implement a crawler list page.
2. Add a file crawler creation form.
3. Add fields for crawler name, root directory, recursion, include/exclude rules, and limits.
4. Support editing and deleting crawler definitions.
5. Add server-side configuration validation.
6. Implement a crawler connection or path-access test.
7. Display crawler enabled/disabled state.
8. Provide clear confirmation for destructive operations.
9. Persist configurations in SQLite.
10. Support running a configured crawler manually.
Example UI
Create File Crawler

Name:
[ Company Documents              ]

Root Directory:
[ \\fileserver\shared\documents   ]

[✓] Include subdirectories

File Types:
[ .pdf,.docx,.pptx,.xlsx,.txt    ]

Excluded Directories:
[ node_modules,bin,obj,.git      ]

Maximum File Size:
[ 100 ] MB

[ Test Path ]   [ Cancel ]   [ Save ]


Acceptance criteria
- Administrators can create and modify file crawlers without editing configuration files.
- Invalid directories and unsafe settings are rejected.
- Saved definitions can be executed by the crawler service.
- UI changes do not require application restarts.

Phase 7 — Crawler Execution Management
Objective: Provide reliable background execution, scheduling, and observable crawler status.
Tasks
1. Implement a persistent crawler job model.
2. Introduce job states: Pending, Running, Completed, Failed, and Canceled.
3. Add a bounded execution queue.
4. Implement manual start, cancellation, and retry.
5. Persist execution history in SQLite.
6. Record processed, indexed, skipped, and failed document counts.
7. Add configurable recurring schedules.
8. Prevent overlapping execution of the same crawler.
9. Recover safely from unexpected application shutdowns.
10. Create crawler job and execution detail pages.
11. Display recent failures and basic progress indicators.
12. Support configurable log retention.
Acceptance criteria
- Administrators can start and cancel crawls.
- Job history survives an application restart.
- Scheduled jobs run without opening the browser.
- A crashed job is not silently marked as completed.
- Concurrent execution is bounded.
- Job progress and errors are visible through the administration UI.

Phase 8 — Web Crawler
Objective: Discover and index websites and intranet pages.
Tasks
1. Introduce a web crawler configuration type.
2. Implement HTTP fetching using HttpClient.
3. Extract page titles, readable text, metadata, and links.
4. Normalize discovered URLs and remove fragments.
5. Restrict crawling to configured host and path boundaries.
6. Add maximum depth and page-count limits.
7. Support URL include/exclude patterns.
8. Respect robots.txt and configured crawl delays.
9. Implement per-host request throttling.
10. Support redirects with destination validation.
11. Prevent duplicate URLs and crawl loops.
12. Extract and index linked PDF or Office documents when enabled.
13. Reuse the persistent execution and scheduling infrastructure.
14. Add web crawler configuration and status UI.
15. Test against a deterministic local HTTP fixture website.
Acceptance criteria
- A configured website can be crawled and searched.
- Links outside allowed boundaries are not followed.
- Repeated crawls update changed content.
- Deleted or missing pages are reconciled without accidental mass deletion.
- Crawling terminates at configured limits.
- Credentials, internal metadata, and Meilisearch keys are not exposed through crawling errors.
Milestone: Lantern supports both local files and websites.

Phase 9 — Search Experience Refinement
Objective: Transform the prototype into a consistent production-facing search interface.
Tasks
1. Add highlighted result snippets.
2. Add document-type filtering.
3. Add filtering by crawler or source.
4. Display result counts and pagination.
5. Add sorting options where supported.
6. Improve document titles and metadata presentation.
7. Implement safe file and web source navigation.
8. Preserve search state in URL query parameters.
9. Provide keyboard-accessible search controls.
10. Improve empty, loading, and error states.
11. Test Japanese and English search behavior.
12. Validate search performance against a representative dataset.
Acceptance criteria
- Search results provide understandable source information.
- Filters work consistently.
- Results never display raw, untrusted HTML.
- Search URLs can be shared and revisited.
- The interface remains responsive with large result sets.

Phase 10 — Security and Reliability
Objective: Harden Lantern for operation inside a trusted but not necessarily isolated intranet.
Tasks
1. Review administrator authentication and authorization.
2. Enforce antiforgery protection and validate state-changing requests.
3. Restrict crawler configuration and execution to administrators.
4. Validate search inputs and bound expensive queries.
5. Prevent SSRF through web crawler DNS, IP, port, and redirect checks.
6. Restrict local file crawler roots to explicitly approved directories.
7. Prevent path traversal and unauthorized file access.
8. Ensure Meilisearch listens only on an internal interface.
9. Use least-privilege credentials for the application and crawler.
10. Introduce bounded extraction resources and failure isolation.
11. Protect secrets using appropriate Windows or ASP.NET Core mechanisms.
12. Test backup and recovery of SQLite and Meilisearch data.
13. Validate safe recovery from partially completed crawl/index operations.
14. Document supported security boundaries and limitations.
Acceptance criteria
- Unauthorized users cannot access administration functions.
- Unapproved network targets and filesystem paths are rejected.
- Invalid or malicious input cannot bypass configured crawl restrictions.
- Query parameters cannot circumvent server-side authorization.
- The system recovers from ordinary service interruptions without losing crawler configuration.
- Production operation does not expose Meilisearch directly to intranet clients.

Phase 11 — CI and Quality Assurance
Objective: Ensure consistent builds, deterministic tests, and repeatable releases.
Tasks
1. Create GitHub Actions workflows.
2. Build all projects in Release configuration.
3. Run unit and integration tests.
4. Start a native Meilisearch process for integration tests.
5. Validate document extraction fixtures.
6. Add crawler behavior and failure-recovery tests.
7. Run formatting and static analysis checks.
8. Add dependency vulnerability checks.
9. Add third-party license review checks.
10. Produce test and build artifacts.
11. Test Windows x64 publishing.
12. Ensure build failures block release creation.
Acceptance criteria
- CI works without Docker.
- Tests produce reproducible results.
- Search, indexing, crawling, and persistence integrations are covered.
- Vulnerable dependencies and release-blocking license issues are reported.
- Every release artifact is traceable to a commit.

Phase 12 — Windows Packaging and Release Preparation
Objective: Ship a Windows-first Lantern distribution that requires minimal setup.
Tasks
1. Publish Lantern as a self-contained Windows x64 application.
2. Bundle the verified Meilisearch Community Edition executable.
3. Implement Meilisearch process startup and shutdown management.
4. Create required application data directories on first launch.
5. Initialize SQLite and Meilisearch configuration safely.
6. Generate or provision secure Meilisearch credentials.
7. Support configurable HTTP/HTTPS binding for intranet use.
8. Provide a Windows service installation mechanism.
9. Handle graceful application and search-engine shutdown.
10. Package license notices and third-party attributions.
11. Add version information and checksums to release artifacts.
12. Write installation, configuration, upgrade, backup, and troubleshooting documentation.
13. Test fresh installation, upgrade, restart, and removal procedures.
14. Create an automated GitHub release workflow.
Proposed distribution
Lantern-v1.0.0-win-x64.zip
└── Lantern/
    ├── Lantern.exe
    ├── meilisearch.exe
    ├── appsettings.json
    ├── licenses/
    ├── wwwroot/
    └── README.md


Runtime data should be created outside the installation directory, under a configurable location such as:
%ProgramData%\Lantern\
├── config/
├── database/
├── meilisearch/
└── logs/


Acceptance criteria
- Lantern starts on a clean, supported Windows x64 installation.
- No separate .NET runtime or Docker installation is required.
- Meilisearch starts automatically with Lantern.
- User configuration and index data survive application upgrades.
- The application can run as a Windows service.
- An intranet browser can connect using the configured endpoint.
- All required license files are included.
- The release archive passes installation and smoke tests.
Milestone: Lantern v1.0.0 is ready for an initial public release.

4. Milestone Definitions
Milestone	Completed phases	Result
Search Prototype	0–1	Search an existing Meilisearch index
Document Search MVP	0–5	Index and search local documents
Managed File Search	0–7	Configure and schedule file crawlers via browser
Feature Complete	0–9	File and web crawling with full search UI
Release Candidate	0–11	Security and automated QA completed
Lantern v1.0	0–12	Windows distribution ready
5. Coding Agent Execution Rules
The following instructions apply to every implementation phase.
General instructions
- Implement only the currently assigned phase.
- Read the existing code and documentation before modifying the architecture.
- Preserve completed behavior from previous phases.
- Keep changes focused and avoid unrelated refactoring.
- Prefer built-in .NET functionality over additional packages.
- Introduce abstractions only when they solve an immediate requirement.
- Do not replace Meilisearch, Toxy, SQLite, or Blazor without documented justification.
- Do not introduce Docker, Node.js, or Java as runtime requirements.
- Add automated tests for meaningful new behavior.
- Avoid placeholder implementations and simulated production functionality.
- Do not claim that integration tests passed unless they were actually executed.
Required completion report
At the end of each phase, provide:
1. Summary of implemented functionality.
2. List of files created or modified.
3. Key architectural decisions.
4. Build and test commands executed.
5. Test results.
6. Known limitations.
7. Remaining work, if any.
Do not begin the next phase automatically.
6. Deferred Enhancements After v1.0
These features may be considered after the initial release:
- Windows Integrated Authentication (Active Directory / Kerberos).
- Per-source and per-document access control.
- Search synonyms and relevance-tuning administration.
- Richer crawl diagnostics and performance statistics.
- Automatic crawler configuration import/export.
- OCR for scanned PDF files.
- Additional document formats.
- Incremental filesystem change notifications.
- Linux support.
- An optional installer with a graphical setup wizard.
Keep these features out of the initial implementation unless they are necessary to fix a release-blocking issue.

7. Definition of Done for Lantern v1.0
Lantern v1.0 is complete when an administrator can install the application on Windows, open the administration UI in a web browser, create a file or web crawler, configure its boundaries and schedule, run it, monitor progress, and search indexed content without external infrastructure beyond the bundled components.
The release must operate without Docker, protect administrative functions, handle crawler failures safely, and provide reproducible builds and tests.

実装上の補足
このロードマップで、特に重要だと考える設計判断は3つあります。
1. Phase 1 を最初の目に見える成果にする
最初からクローラフレームワークを実装すると、検索結果のデータモデルや UI に必要な情報が固まる前に基盤が肥大化しがちです。まず検索画面を作り、その画面で必要な情報を基準にインデックス設計を確定させます。
2. SQLite と Meilisearch の責務を明確に分ける
Meilisearch は検索データ、SQLite はクローラ設定・実行履歴・差分検出の状態を保持します。これによって検索インデックスを再構築しても、クローラの管理情報は維持できます。また、初期段階では分散ジョブキューも不要です。
3. Lantern の強みを管理 UI のわかりやすさに置く
検索 UI はシンプルにし、管理 UI は「クローラを作成する → 実行する → 進行状況を見る → 検索する」の流れを迷わず操作できることを重視します。
この方針なら、Phase 0〜7 で実用的なファイル全文検索システムが成立し、Phase 8 以降で Fess の代替として必要な Web クローリングと運用品質を追加するという、Coding Agent にも取り組ませやすい段階的な開発ができます。
