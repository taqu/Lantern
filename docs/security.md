# Lantern Security Model

## Authentication

Lantern uses ASP.NET Core cookie authentication for the administration interface.

- Credentials are stored as Argon2id (via `IPasswordHasher<T>`) hashes in SQLite.
- The initial administrator account is provisioned at startup; plaintext passwords are never persisted.
- The authentication cookie is named `__Host-Lantern.Admin` with `HttpOnly`, `Secure`, and `SameSite=Strict` attributes.
- Sessions expire after 60 minutes of inactivity (sliding expiration).
- ASP.NET Core Data Protection keys are persisted to the configured `Storage:DataProtectionKeysPath` directory so sessions survive controlled restarts.
- Login attempts are rate-limited (ASP.NET Core rate limiting, 5 attempts per IP per minute); repeated failures return HTTP 429.
- Login errors use a generic "invalid credentials" message to prevent username enumeration.

## Authorization

All administration routes (`/admin/**`) are protected by the `Administrator` policy, which requires the user to hold the `Administrator` role claim. The policy is enforced globally in `src/Lantern.Web/Components/Pages/Admin/_Imports.razor`.

Privileged service operations (e.g., `AdministrationService.SaveSettingsAsync`) enforce authorization independently of the UI layer, so a direct service call from an unauthenticated context throws `UnauthorizedAccessException`.

The public search page (`/search`) is intentionally unauthenticated.

## CSRF Protection

ASP.NET Core antiforgery middleware is applied via `UseAntiforgery()`. All state-changing HTTP POST actions in the administration interface require a valid `__RequestVerificationToken`. Requests missing this token return HTTP 400.

Blazor Interactive Server components use the ASP.NET Core antiforgery infrastructure automatically for form submissions.

## Security Response Headers

All HTTP responses include the following headers:

| Header | Value |
|--------|-------|
| `X-Content-Type-Options` | `nosniff` |
| `X-Frame-Options` | `DENY` |
| `Referrer-Policy` | `strict-origin-when-cross-origin` |
| `X-Permitted-Cross-Domain-Policies` | `none` |

HSTS is enabled in non-development environments via `UseHsts()`.

## Secret Handling

- The Meilisearch master key and API key are read from configuration (`appsettings.json` or environment variables) and never logged or returned to the browser.
- The administration settings page explicitly strips the API key from any displayed values.
- Job configuration snapshots never include credentials.
- Data Protection keys are stored in `Storage:DataProtectionKeysPath` (default: `./keys`). This directory should be readable only by the Lantern process account.

## Network Isolation

**Meilisearch** must be bound to loopback (`127.0.0.1`) by default. It is not exposed through any public HTTP route. The Lantern ASP.NET Core process is the only intended client.

**Embedding REST server** (LlamaPure) binds to loopback and is not reachable from intranet clients. Its endpoint is not proxied or exposed through Lantern's public routes.

Note: loopback binding does not prevent other processes on the same machine from reaching these services. Process-level OS isolation or firewall rules are required for stricter enforcement.

## Web Crawler SSRF Protection

The web crawler validates every URL (start URLs, discovered links, redirect targets) through `CrawlBoundaryValidator`:

- Only `http` and `https` schemes are allowed.
- Embedded URL credentials (`user:pass@host`) are rejected.
- Hostnames are resolved via `SocketsHttpHandler.ConnectCallback` at connection time to prevent DNS TOCTOU rebinding: the address is checked when the TCP connection is established, not just at crawl-start validation.
- Private, loopback, and link-local addresses are blocked unless explicitly approved in `AllowedNetworkRanges` configuration.
- Cloud metadata endpoints (`169.254.169.254`, `[fd00:ec2::254]`, etc.) are always blocked.
- Redirect destinations are validated independently before following.
- Automatic redirects are disabled on the crawler `HttpClient`; redirects are handled manually.

Intranet targets can be approved via the `AllowedHosts` / `AllowedNetworkRanges` configuration on each web crawler definition.

## Filesystem Crawler Boundaries

The file crawler enforces the following:

- The root path must be an absolute path approved by an administrator.
- Every discovered subdirectory is validated as a descendant of the canonical root path (resolving drive letter casing and trailing separator differences).
- Reparse points (symbolic links, NTFS junctions) are detected via `FileAttributes.ReparsePoint` and **skipped** for both files and directories. This prevents a symlink inside an approved root from exposing files outside it.
- UNC paths (`\\server\share`) are supported as roots; access failures are treated as crawl errors rather than triggering deletion.

## Search Input and Output Safety

- Query strings are validated by `SearchQueryValidator` (max 500 characters, basic sanity checks).
- Meilisearch filter expressions are constructed from server-controlled field names only. User-supplied values (category keys, source types) are escaped via `EscapeFilterValue` before embedding in filter strings.
- Search result titles, paths, and snippets are rendered as plain text in Blazor components — HTML encoding is applied automatically by Razor; no raw `@Html.Raw` is used.
- External source URLs are validated by `UrlValidator.IsSafeDisplayUrl` before rendering as hyperlinks.

## Search Access Model (v1.0 Limitation)

Lantern v1.0 does not enforce per-document Windows ACL permissions on search results. Any authenticated user who can reach the search page can find any indexed document regardless of the original filesystem permissions.

This is a known v1.0 limitation. Search categories are organizational filters, not access-control boundaries.

## Logging

- Structured logging via `Microsoft.Extensions.Logging`.
- Passwords and API keys are not logged.
- Extracted document content is not logged at default levels.
- Embedding vectors are not logged.
- Detailed error information (stack traces) is suppressed in production HTTP responses; it appears only in server logs.
