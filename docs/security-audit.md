# Lantern Security Audit — Phase 10

**Date**: 2026-10-21  
**Scope**: Phases 0–9 implementation  
**Reviewer**: Phase 10 automated audit

---

## 1. Reviewed Components

| Component | Reviewed |
|-----------|---------|
| Authentication (`AdministratorAuthentication`) | Yes |
| Authorization (`[Authorize]` policy, `AdministrationService`) | Yes |
| CSRF (`UseAntiforgery`, form tokens) | Yes |
| Session cookies (`__Host-` prefix, security attributes) | Yes |
| Rate limiting (login endpoint) | Yes |
| Security response headers | Yes |
| Meilisearch configuration and isolation | Yes |
| Meilisearch filter expression construction | Yes |
| Web crawler URL validation (`CrawlBoundaryValidator`) | Yes |
| Web crawler redirect handling | Yes |
| Web crawler DNS rebinding protection (`SocketsHttpHandler.ConnectCallback`) | Yes |
| Web crawler robots.txt | Yes |
| Web crawler rate limiting (`HostRateLimiter`) | Yes |
| Filesystem crawler root validation | Yes |
| Filesystem crawler boundary enforcement (`IsDescendantOf`) | Yes |
| Filesystem crawler reparse point handling | Yes |
| Document extraction resource limits | Yes |
| Search query validation (`SearchQueryValidator`) | Yes |
| Search result rendering (XSS) | Yes |
| Source URL validation (`UrlValidator`) | Yes |
| SQLite migrations and lifecycle | Yes |
| Job persistence and recovery | Yes |
| Category synchronization | Yes |
| Secret handling in configuration and logs | Yes |
| Embedding server isolation | Yes |

---

## 2. Identified Vulnerabilities and Findings

### MEDIUM-1 — Missing Security Response Headers

**Severity**: Medium  
**Component**: `Lantern.Web` — HTTP middleware  
**Description**: Before Phase 10, HTTP responses lacked standard security headers (`X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `X-Permitted-Cross-Domain-Policies`). A missing `X-Frame-Options: DENY` allows the admin UI to be embedded in a frame, enabling clickjacking attacks against authenticated administrators.  
**Mitigation**: Added middleware in `Program.cs` that injects all four headers on every response. HSTS was already present for non-development environments.  
**Verification**: `SecurityHeadersAreSetOnEveryResponse` test in `AdministrationHttpTests`.  
**Remaining risk**: Content-Security-Policy (CSP) was not added in this phase due to complexity of Blazor Interactive Server's inline script requirements. CSP hardening is deferred.

---

### MEDIUM-2 — Symbolic Link / Junction Traversal

**Severity**: Medium  
**Component**: `Lantern.Crawling` — `DirectoryEnumerator`  
**Description**: Before Phase 10, the `DirectoryEnumerator` did not check `FileAttributes.ReparsePoint` on directories or files. An NTFS junction or symlink inside an approved root directory could point outside it, causing the crawler to read files from unapproved paths. The `IsDescendantOf` boundary check would not prevent this because the junction itself is inside the root; the filesystem resolves the target transparently.  
**Mitigation**: Added explicit `FileAttributes.ReparsePoint` check for both files and directory entries. Reparse points are skipped with a debug log entry.  
**Verification**: `FilesystemSecurityTests.Junction_DirectoryReparsePointIsSkipped` and `FilesystemSecurityTests.Symlink_FileReparsePointIsSkipped`.  
**Remaining risk**: Tests conditionally skip when junction/symlink creation requires elevated privileges. The code fix is unconditional.

---

### LOW-1 — Meilisearch URL Not Validated at Startup

**Severity**: Low  
**Component**: `Lantern.Infrastructure` — `MeilisearchOptions`  
**Description**: The Meilisearch URL (`Meilisearch:Url`) is used as-is without verifying at startup that it binds to a loopback address. In production a misconfigured URL pointing to a public address would expose Meilisearch queries to the network.  
**Mitigation**: Documentation in `security.md` and `operations.md` notes that Meilisearch must be bound to loopback. Runtime validation was not added in Phase 10 to avoid breaking existing test configurations that use non-loopback test addresses.  
**Remaining risk**: A misconfigured URL is not rejected at startup. Operators must verify this manually.

---

### LOW-2 — No Content-Security-Policy Header

**Severity**: Low  
**Component**: `Lantern.Web` — HTTP middleware  
**Description**: No `Content-Security-Policy` header is set. Blazor Interactive Server emits inline scripts which require `'unsafe-inline'` in the script-src directive, making a strong CSP policy difficult to implement without nonce-based CSP.  
**Mitigation**: The missing `X-Frame-Options: DENY` (which CSP can also enforce via `frame-ancestors 'none'`) was addressed by the dedicated header. Full CSP is deferred.  
**Remaining risk**: Without CSP, reflected XSS (if introduced in a future change) would have one fewer mitigating layer.

---

### LOW-3 — Per-Document Permission Enforcement Absent

**Severity**: Low (documented v1.0 limitation)  
**Component**: All search paths  
**Description**: Lantern indexes documents from the crawling service account's perspective. Search results are not filtered by the requesting user's filesystem ACLs. Any user who can reach the search page can find any indexed document.  
**Mitigation**: Documented in `security.md` as a known v1.0 limitation ("common-access search model"). Categories are organizational, not access-control boundaries.  
**Remaining risk**: Content from access-restricted directories should not be added to approved crawler roots unless all search users are permitted to see it.

---

### INFORMATIONAL-1 — Embedding Server Has No Application-Level Authentication

**Severity**: Informational  
**Component**: `Lantern.Embedding`  
**Description**: The embedding REST server binds to loopback only and has no HTTP-level API key. It is reachable by any local process. This is acceptable for a single-machine deployment where the OS provides process isolation.  
**Mitigation**: Binding to loopback is verified in configuration. Documented in `security.md`.  
**Remaining risk**: Other local processes can submit embedding requests. Not considered a significant risk for the intended single-host deployment model.

---

### INFORMATIONAL-2 — Job Snapshots Contain Crawler Configuration

**Severity**: Informational  
**Component**: `Lantern.Crawling` — job snapshots  
**Description**: Job snapshots include crawler configuration (root paths, URLs) but not credentials. Root paths and URLs are considered non-secret operational configuration.  
**Mitigation**: Verified that no passwords or API keys appear in snapshots.  
**Remaining risk**: Root paths are stored in plaintext in the SQLite job history. This is acceptable for an intranet deployment.

---

## 3. Implemented Mitigations

| Finding | Fix | File |
|---------|-----|------|
| MEDIUM-1: Missing security headers | Added middleware in `Program.cs` | `src/Lantern.Web/Program.cs` |
| MEDIUM-2: Reparse point traversal | Added `FileAttributes.ReparsePoint` check | `src/Lantern.Crawling/Files/DirectoryEnumerator.cs` |

---

## 4. Remaining Risks

| Risk | Severity | Notes |
|------|----------|-------|
| No Content-Security-Policy | Low | Deferred; requires Blazor nonce support |
| Meilisearch URL not validated at startup | Low | Operator responsibility |
| No per-document ACL enforcement | Low | Documented v1.0 limitation |
| Embedding server unauthenticated locally | Informational | Loopback binding is sufficient |

---

## 5. Security Assumptions

1. The server host is administered by a trusted operator.
2. The intranet network is partially trusted (not all clients are trusted).
3. All administrator accounts are legitimate — no multi-tenant isolation is required.
4. The SQLite database file is protected by OS-level filesystem permissions.
5. Meilisearch is bound to loopback and not reachable from intranet clients.
6. The GGUF model file is obtained from a trusted source and not user-supplied.
7. TLS termination (HTTPS) is handled by the ASP.NET Core server or a local reverse proxy.

---

## 6. Tests Performed

| Category | Test Class | Count |
|----------|-----------|-------|
| Authentication / login | `AdministrationHttpTests` | 5 tests |
| Authorization (anonymous access) | `AdministrationHttpTests` | 1 test |
| CSRF (antiforgery token) | `AdministrationHttpTests` | Covered in LoginSettings test |
| Cookie security attributes | `AdministrationHttpTests` | 1 test |
| Rate limiting (login) | `AdministrationHttpTests` | 1 test |
| Security headers | `AdministrationHttpTests.SecurityHeadersAreSetOnEveryResponse` | New |
| Category admin authorization | `AdministrationHttpTests.CategoryAdminRequiresAuthentication` | New |
| Open redirect prevention | `AdministrationHttpTests.OpenRedirect_LoginReturnUrlIsRestricted` | New |
| URL normalization | `UrlNormalizerTests` | 18 tests |
| Web crawl boundary validation | `CrawlBoundaryValidatorTests` | 17 tests |
| robots.txt | `RobotsPolicyTests` | 6 tests |
| Host rate limiting | `HostRateLimiterTests` | 7 tests |
| Filesystem boundary | `DirectoryEnumeratorTests` | 8 tests |
| Filesystem security (reparse points) | `FilesystemSecurityTests` | 8 tests (new) |
| Search query validation | `SearchQueryValidatorTests` | Multiple |
| SQLite persistence / migrations | `SqlitePersistenceTests` | Multiple |

**Total**: 260 tests (255 pass, 5 skip requiring live Meilisearch, 0 fail)

---

## 7. Tests Not Performed

| Area | Reason |
|------|--------|
| Live Meilisearch security tests | Require running Meilisearch instance; 5 tests marked Skip |
| Junction creation (requires elevation) | Tests skip gracefully when `mklink /J` fails |
| Network-level Meilisearch isolation | Would require firewall inspection; out of scope |
| Extraction fuzzing (malformed Office/PDF) | Covered by `ExtractionTests` with real malformed fixtures; automated fuzzing not in scope |
| Backup/restore integration test | Procedure documented; automated test not implemented in Phase 10 |
| DNS rebinding under real network | Covered by `CrawlBoundaryValidatorTests` with fake DNS; live DNS rebinding test not in scope |

---

## 8. Deployment Requirements

1. Run Meilisearch bound to `127.0.0.1:7700` only.
2. Run Lantern under a least-privilege Windows service account.
3. Grant that account read access to approved crawler source paths.
4. Grant write access to `./data/`, `./keys/`, and Meilisearch data directory.
5. Configure HTTPS (Kestrel certificate or reverse proxy).
6. Set `Storage:DataProtectionKeysPath` to a persistent directory.
7. Protect the database and keys directory from read access by non-Lantern accounts.
8. Do not expose port 7700 (Meilisearch) or the embedding port on intranet-accessible interfaces.

---

## 9. Known v1.0 Limitations

1. No per-document Windows ACL synchronization — all indexed documents are visible to all search users.
2. No Content-Security-Policy header — mitigated by output encoding in Blazor components.
3. No automated backup procedure — documented manual procedure provided.
4. Meilisearch URL not validated at startup — operator must verify loopback binding.
5. Single administrator account — no multi-user administration.
6. Categories are not access-control boundaries.
7. Loopback service binding does not prevent local-process access to Meilisearch or the embedding server.
