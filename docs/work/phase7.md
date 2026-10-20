# Lantern — Phase 7 Implementation Instructions
## Persistent Crawler Job Execution, Scheduling, and Monitoring

You are implementing **Phase 7: Crawler Execution Management** of the Lantern project.

Lantern is a lightweight, self-hosted full-text search platform designed primarily for Windows and intranet environments.

The application uses:

- .NET 10 and ASP.NET Core.
- Blazor Web App with Interactive Server.
- Meilisearch Community Edition.
- SQLite with Entity Framework Core.
- LlamaPure with IBM Granite Embedding 97M Multilingual R2.
- Local REST-based embedding and hybrid search.
- Document extraction and structure-aware chunking.
- Incremental local filesystem crawling.
- Browser-based crawler configuration and administration.

Phases 0–6 have established the search interface, indexing infrastructure, administration foundation, hybrid search, document extraction, incremental file crawling, and browser-based crawler management.

**Your task is to implement reliable persistent crawler job execution, recurring scheduling, progress reporting, cancellation, retry, execution history, and recovery from application shutdowns or crashes.**

The resulting execution infrastructure must be reusable by the future Phase 8 web crawler.

Do not implement web crawling or unrelated subsequent roadmap features.

---

## 1. Review the Existing Implementation

Before modifying the repository:

1. Read `roadmap.md` and existing architecture documentation.
2. Inspect the current solution and project structure.
3. Review the Phase 3 SQLite persistence and migrations.
4. Review the Phase 5 file crawler execution service.
5. Review Phase 5 incremental synchronization and reconciliation.
6. Review Phase 6 manual execution coordination.
7. Review existing administration authentication and authorization.
8. Review the current Crawlers and Jobs administration pages.
9. Review the existing Meilisearch indexing and task completion services.
10. Review embedding server lifecycle and hybrid search integration.
11. Inspect application startup and shutdown behavior.
12. Run existing builds and tests to establish a baseline.

The actual repository is the source of truth.

Preserve existing functionality and established conventions.

**Extend or replace the Phase 6 in-memory manual execution coordinator with the persistent job infrastructure. Do not leave two independent crawler execution systems.**

Avoid unrelated architectural refactoring.

## 2. Phase Objectives

Implement:

1. Persistent crawler job records.
2. Job states and state transitions.
3. Bounded background execution.
4. Manual job submission.
5. Recurring crawler schedules.
6. Job cancellation.
7. Job retry.
8. Persistent execution history.
9. Progress counters and timestamps.
10. Per-crawler execution exclusion.
11. Safe crash and restart recovery.
12. Job monitoring administration pages.
13. Execution diagnostics.
14. Configurable history and log retention.
15. Unit and integration tests.

The execution system must work independently of browser sessions.

Do not require an administrator to keep a browser tab open while a crawler runs.

## 3. Architecture

Use the existing Lantern services to implement:

```text
Blazor Administration UI
          |
          v
Crawler Job Service
          |
          v
SQLite Persistent Job Store
          |
          v
Background Job Dispatcher
          |
          v
Bounded Execution Queue
          |
          v
Crawler Execution Worker
          |
          v
Existing File Crawler
          |
          +-- File Enumeration
          +-- Incremental Detection
          +-- Document Extraction
          +-- Structure-Aware Chunking
          +-- Meilisearch Indexing
          +-- Hybrid Embedding
          +-- Safe Reconciliation
          |
          v
Progress / Completion / Failure
          |
          v
SQLite Execution History
          |
          v
Blazor Administration UI
```

### Responsibilities

**Job service**

Accepts manual and scheduled execution requests.

**Persistent job store**

Maintains job state and execution history.

**Scheduler**

Determines when recurring crawls are due.

**Dispatcher**

Selects eligible pending jobs and manages execution capacity.

**Execution worker**

Invokes existing crawler services.

**Progress reporter**

Tracks actual crawler progress.

**Administration UI**

Displays jobs, status, history, and available actions.

### Architectural restrictions

Use .NET hosted services and SQLite.

Do not introduce:

- Hangfire.
- Quartz.NET unless an existing dependency clearly requires it.
- RabbitMQ.
- Redis.
- Distributed job systems.
- Separate worker processes.
- External scheduling services.

Prefer a simple internal scheduler and persistent queue.

## 4. Persistent Job Model

Create or extend a persistent crawler job entity.

Suggested model:

```text
CrawlerJob
    |
    +-- Id
    +-- CrawlerId
    +-- TriggerType
    +-- Status
    +-- CreatedAt
    +-- ScheduledAt
    +-- StartedAt
    +-- FinishedAt
    +-- AttemptNumber
    +-- ConfigurationSnapshot
    +-- ErrorCode
    +-- ErrorMessage
    +-- Progress
```

Adapt this structure to the existing repository.

### Job states

Implement:

```text
Pending
Running
Completed
Failed
Canceled
```

Use `Canceled` consistently with the original roadmap.

### Trigger types

Distinguish:

```text
Manual
Scheduled
Retry
```

### Requirements

- Assign a unique job identifier.
- Associate every job with a crawler.
- Persist creation and execution timestamps.
- Store timestamps in UTC.
- Preserve the trigger type.
- Track the execution attempt.
- Preserve useful failure information.
- Persist job progress.
- Preserve history across restarts.

### Configuration snapshots

A job must execute against a consistent crawler configuration.

Capture an immutable validated configuration snapshot at submission or dispatch time.

Changes to crawler configuration must not silently alter an already running job.

Document whether pending jobs use submission-time or dispatch-time configuration.

Prefer submission-time snapshots for predictable manual and retry behavior.

Do not store secrets directly in job snapshots.

Resolve sensitive credentials through the existing protected configuration mechanisms.

## 5. Job State Machine

Implement explicit and validated state transitions.

### Expected transitions

```text
Pending
   |
   +----> Running
   |          |
   |          +----> Completed
   |          |
   |          +----> Failed
   |          |
   |          +----> Canceled
   |
   +----> Canceled
```

Retries should create a new attempt or job linked to the original failed execution.

Preserve historical records instead of overwriting previous attempts.

### Requirements

- Prevent invalid state transitions.
- Record start and completion timestamps.
- Prevent completed jobs from being executed again.
- Prevent duplicate job claims.
- Make state updates concurrency-safe.
- Ensure exceptions cannot silently leave a job marked successful.

A job is `Completed` only when the required crawler operation and its synchronization steps have completed successfully.

If a crawler returns a result containing recoverable file errors, preserve those diagnostics. Define and document whether such a run is considered completed with warnings or failed.

Do not discard partial-failure information merely because the top-level job state is `Completed`.

## 6. SQLite Job Persistence

Use the existing SQLite database and EF Core infrastructure.

Introduce tracked migrations.

Suggested logical tables:

```text
CrawlerJobs
CrawlerSchedules
CrawlerJobEvents
```

Only create additional tables if justified by the implementation.

### Requirements

- Persist job metadata.
- Persist job status.
- Persist schedule definitions.
- Persist execution progress.
- Persist failure diagnostics.
- Persist historical executions.
- Support querying by crawler and status.
- Support bounded history retrieval.

### Database indexes

Add appropriate indexes for:

- Job status.
- Scheduled execution time.
- Crawler ID.
- Creation time.
- Execution history lookup.

### Concurrency safety

SQLite is the authoritative persistent job store.

Do not rely exclusively on in-memory job state.

Use atomic updates or optimistic concurrency checks when claiming jobs.

For example, a dispatcher must not claim a job that another dispatcher has already transitioned from `Pending` to `Running`.

Do not share a single EF Core DbContext between background workers.

Reuse `IDbContextFactory<TContext>` or the established scoped service pattern.

### Persistence limitations

Do not introduce distributed transaction mechanisms.

SQLite and Meilisearch do not share a transaction.

Design jobs to be safely retryable after partial failures.

## 7. Persistent Queue and Dispatcher

Implement a bounded background job dispatcher.

### Core requirements

1. Persist a job before acknowledging submission.
2. Select eligible pending jobs from SQLite.
3. Respect maximum concurrent execution settings.
4. Prevent overlapping jobs for the same crawler.
5. Transition claimed jobs to `Running` atomically.
6. Dispatch work to background execution workers.
7. Persist completion or failure.
8. Continue processing subsequent eligible jobs.

### Important design rule

The in-memory channel is not the authoritative queue.

SQLite is the source of truth.

An in-memory channel may be used as a bounded wake-up or dispatch mechanism.

If the application crashes after inserting a job but before sending an in-memory notification, the job must still be discovered after restart.

### Suggested defaults

```text
MaximumConcurrentJobs = 2
QueueCapacity = 32
DispatcherPollIntervalSeconds = 2
```

These are initial development defaults.

Make limits configurable.

### Backpressure

Do not launch an unlimited number of tasks.

Use bounded execution capacity.

Avoid buffering an unbounded number of pending jobs in memory.

A large backlog may remain in SQLite until execution capacity becomes available.

### Submission behavior

Return a job identifier after persistence succeeds.

Do not wait for the entire crawl to complete before returning to the administration UI.

## 8. Per-Crawler Execution Exclusion

Only one execution of the same crawler may run at a time.

### Requirements

- Prevent concurrent manual and scheduled runs of the same crawler.
- Prevent repeated manual clicks from starting overlapping work.
- Prevent retries from overlapping the original execution.
- Keep other eligible crawlers executable.
- Enforce exclusion in the backend, not only in the UI.

### Persistent coordination

Use concurrency-safe job claiming and crawler ownership.

Do not rely only on an in-memory `SemaphoreSlim`.

Consider storing a running-job ownership relationship or using a transactional claim mechanism.

The solution should prevent overlapping execution within one Lantern application instance.

Document that multi-instance distributed execution is outside the current scope.

### Pending duplicates

Define predictable behavior when multiple jobs are requested for the same crawler.

A suitable initial policy is:

- Reject redundant manual submissions when an active or pending job already exists.
- Coalesce redundant scheduled triggers.
- Preserve explicit retries as separate historical attempts.

Document the selected policy.

## 9. Manual Job Submission

Replace Phase 6 direct manual execution with persistent job submission.

### Expected workflow

```text
Administrator
    |
    v
Click Run
    |
    v
Validate Crawler
    |
    v
Create Pending Job
    |
    v
Persist to SQLite
    |
    v
Return Job ID
    |
    v
Background Dispatcher
```

### Requirements

- Administrator authorization is required.
- The crawler must exist.
- The crawler must be enabled.
- Configuration must be valid.
- Duplicate execution must be prevented.
- Submission must persist before returning success.
- The UI must provide a link to job details.

Do not execute the crawler directly within a Blazor component event handler.

Manual execution must continue when the browser disconnects.

## 10. Recurring Scheduling

Implement a simple persistent internal scheduler.

Administrators must be able to configure recurring crawler execution.

### Recommended initial schedule types

Support:

- Disabled.
- Hourly interval.
- Daily at a specified time.
- Weekly on selected days at a specified time.

An interval-based schedule is also acceptable if it satisfies the intended recurring execution requirements.

Do not implement a complex general-purpose cron engine unless already available.

### Example schedule

```json
{
  "crawlerId": "company-documents",
  "enabled": true,
  "scheduleType": "daily",
  "time": "02:00",
  "timeZone": "Asia/Tokyo"
}
```

The example is illustrative.

### Scheduling requirements

- Persist schedule configuration in SQLite.
- Calculate the next scheduled occurrence.
- Store next execution timestamps in UTC.
- Use a defined time zone for human-readable schedules.
- Handle daylight-saving transitions predictably.
- Recalculate schedules when configuration changes.
- Prevent duplicate scheduled jobs.
- Execute schedules without an open browser.
- Respect crawler enabled/disabled state.

### Missed executions

Define an explicit policy for schedules missed during application downtime.

Recommended default:

**Coalesce missed occurrences into at most one pending catch-up execution per crawler.**

Do not enqueue hundreds of missed jobs after a long outage.

Advance the next scheduled time correctly.

Avoid running the same scheduled occurrence twice.

### Schedule changes

When a schedule is modified:

- Validate the new definition.
- Persist the change.
- Recalculate the next occurrence.
- Preserve completed execution history.
- Avoid modifying currently running jobs.

Disabling a schedule must not delete indexed documents.

## 11. Background Worker Execution

Use the existing Phase 5 crawler service.

### Requirements

- Load the job's configuration snapshot.
- Create an execution cancellation token.
- Begin reporting progress.
- Invoke the crawler.
- Await its completion.
- Persist the final result.
- Release execution capacity.
- Release per-crawler ownership.

### Exception handling

Catch and classify expected failures.

Unexpected exceptions must:

- Be logged.
- Be reflected in the job result.
- Release execution ownership.
- Avoid permanently blocking the dispatcher.

Do not suppress exceptions without recording the failure.

Do not invent successful results when the crawler returns incomplete synchronization.

### Service lifetime

Use `BackgroundService` or equivalent hosted-service integration.

Respect application startup and shutdown.

Ensure the worker does not depend on Blazor circuit lifetime.

## 12. Progress Reporting

Extend the existing crawler execution API with progress reporting where necessary.

Prefer an interface such as:

```csharp
IProgress<CrawlerProgress>
```

or an existing equivalent.

### Suggested counters

```text
DiscoveredFiles
ProcessedFiles
IndexedFiles
SkippedFiles
FailedFiles
DeletedFiles
```

Additional useful counters may include:

```text
ExtractedDocuments
GeneratedChunks
IndexedChunks
```

Only implement counters that can be accurately measured.

### Progress model

Conceptually:

```csharp
public sealed record CrawlerProgress(
    long DiscoveredFiles,
    long ProcessedFiles,
    long IndexedFiles,
    long SkippedFiles,
    long FailedFiles,
    long DeletedFiles);
```

Adapt to existing naming conventions.

### Requirements

- Report actual observed progress.
- Update progress periodically.
- Persist progress at reasonable intervals.
- Avoid a SQLite write for every discovered file.
- Preserve final counters.
- Allow administrators to see progress while a crawl is running.

### Important distinction

Filesystem discovery may be streaming.

The total number of files may be unknown until enumeration finishes.

Do not fabricate a progress percentage.

Display counters or an indeterminate progress indicator when the total is unknown.

### Meilisearch completion

Do not count a document as successfully indexed merely because Meilisearch accepted the indexing request.

Preserve the Phase 5 requirement to track asynchronous task completion.

If embedding generation or indexing fails, reflect that failure appropriately.

## 13. Job Cancellation

Implement cancellation for pending and running jobs.

### Pending jobs

Allow an administrator to cancel a pending job.

Transition:

```text
Pending -> Canceled
```

A canceled pending job must never start.

### Running jobs

When cancellation is requested:

1. Record the cancellation request.
2. Signal the active execution.
3. Stop accepting additional crawler work.
4. Allow the crawler to terminate safely.
5. Persist the final canceled result.

### Requirements

- Use cooperative cancellation.
- Propagate cancellation into the Phase 5 crawler.
- Avoid unsafe deleted-file reconciliation after cancellation.
- Preserve already completed synchronization state.
- Do not terminate the Lantern process.
- Do not assume native document extraction or embedding inference can be interrupted instantly.

### Persistent cancellation intent

Cancellation requests must be represented in persistent state.

Do not rely exclusively on in-memory `CancellationTokenSource` instances.

If the process crashes after cancellation is requested, recovery must not incorrectly resume the canceled job.

Use a separate cancellation-request flag or equivalent mechanism if necessary.

### UI

Provide Cancel actions for eligible pending and running jobs.

Display the cancellation request while shutdown is in progress.

Do not report `Canceled` until cancellation has actually been resolved, unless the job had not started.

## 14. Job Retry

Implement manual retry for failed or canceled executions where appropriate.

### Requirements

- Preserve the original job record.
- Create a new execution attempt.
- Link the retry to the original job.
- Preserve retry history.
- Validate crawler availability and configuration.
- Prevent overlapping execution.
- Avoid duplicating documents or chunks.

### Incremental recovery

Reuse Phase 5 synchronization behavior.

A retry must safely handle:

- Files already indexed.
- Partially completed document updates.
- Previously successful chunks.
- Failed Meilisearch operations.
- Interrupted deletion reconciliation.

Do not clear crawl state merely to perform a retry.

### Automatic retries

Complex automatic retry policies are not required.

A simple explicit manual retry is sufficient for Phase 7.

Do not implement unbounded automatic retries.

If transient retry behavior already exists in lower-level services, preserve its established limits.

## 15. Application Crash and Restart Recovery

Implement safe recovery of interrupted jobs.

**This is a critical Phase 7 requirement.**

### Failure scenarios

Handle:

- Application crash.
- Unexpected host termination.
- Machine restart.
- Controlled application shutdown.
- Meilisearch interruption.
- SQLite operation failure.
- Crawler exception.

### Startup recovery

On application startup:

1. Initialize the database.
2. Inspect persistent jobs.
3. Identify jobs left in `Running`.
4. Determine which executions were interrupted.
5. Mark interrupted executions as failed or recoverable according to an explicit policy.
6. Release stale execution ownership.
7. Restore dispatching of eligible pending jobs.
8. Recalculate scheduler state.
9. Avoid unsafe filesystem reconciliation.

### Recommended policy

For the initial implementation:

- Mark orphaned `Running` jobs as `Failed`.
- Record an explicit interruption reason.
- Preserve partial progress and diagnostics.
- Preserve existing crawl synchronization state.
- Allow administrators to retry.
- Keep previously persisted `Pending` jobs eligible for execution.

Do not automatically replay an interrupted running crawl unless recovery semantics have been explicitly implemented and tested.

### Data safety

An interrupted scan must not be treated as a completed enumeration.

Do not reconcile file deletions based on incomplete crawl state.

Do not assume that Meilisearch and SQLite were updated atomically.

Use idempotent operations to repair incomplete synchronization during later runs.

### Important limitation

Startup recovery based on an application-local single-instance assumption is acceptable for this phase.

Do not implement distributed leases or leader election.

## 16. Job History

Implement persistent execution history.

### Required information

- Job ID.
- Crawler name.
- Trigger type.
- Status.
- Created time.
- Scheduled time.
- Start time.
- Finish time.
- Duration.
- Progress counters.
- Failure summary.
- Retry relationship.

### Requirements

- Preserve completed and failed job records.
- Support filtering by crawler.
- Support filtering by status.
- Order recent executions first.
- Use server-side pagination.
- Avoid loading the entire history into memory.

### Deleted crawler definitions

Job history should remain understandable when a crawler definition is archived or deleted.

Preserve a safe display name or historical snapshot.

Do not require the original crawler entity to remain active to display its historical executions.

Avoid accidental cascade deletion of execution history.

## 17. Execution Events and Diagnostics

Implement lightweight persistent job events or structured execution summaries.

Suggested events:

```text
JobCreated
JobStarted
ProgressUpdated
JobCompleted
JobFailed
CancellationRequested
JobCanceled
JobInterrupted
JobRetried
```

### Requirements

- Record meaningful lifecycle events.
- Record failure diagnostics.
- Avoid storing full extracted document text.
- Avoid storing embedding vectors.
- Avoid logging credentials.
- Avoid generating excessive event records.

Do not store a separate event for every discovered file by default.

Use existing application logging for detailed operational diagnostics.

Persist concise job-related events that are useful to administrators.

### Error classification

Where possible, distinguish:

- Filesystem access failures.
- Extraction failures.
- Indexing failures.
- Embedding failures.
- Crawler cancellation.
- Application interruption.
- Configuration errors.
- Internal execution failures.

Do not expose sensitive internal exception details in the administration UI.

## 18. Configurable History and Log Retention

Implement basic retention settings.

Suggested configuration:

```json
{
  "CrawlerJobs": {
    "MaximumConcurrentJobs": 2,
    "QueueCapacity": 32,
    "DispatcherPollIntervalSeconds": 2,
    "ProgressPersistenceIntervalSeconds": 5,
    "HistoryRetentionDays": 90,
    "EventRetentionDays": 30
  }
}
```

Values are illustrative.

### Requirements

- Retention must be configurable.
- Do not delete active jobs.
- Do not delete pending jobs.
- Do not delete active schedule definitions.
- Remove obsolete historical records safely.
- Avoid deleting records required for recovery or active retry relationships.
- Perform cleanup without blocking normal crawler execution.

Keep retention implementation straightforward.

Do not build a general-purpose log archival system.

## 19. Administration UI

Extend the existing Phase 6 administration interface.

### Required routes

Suggested routes:

```text
/admin/jobs
/admin/jobs/{id}
/admin/crawlers/{id}/schedule
```

Follow existing repository conventions.

### Jobs list

Display:

| Column | Description |
|---|---|
| Job ID | Execution identifier |
| Crawler | Crawler name |
| Trigger | Manual, Scheduled, Retry |
| Status | Current execution state |
| Started | Execution start |
| Duration | Elapsed or completed duration |
| Progress | Available counters |
| Actions | Details, Cancel, Retry |

### Job details

Display:

- Crawler information.
- Job status.
- Trigger type.
- Configuration snapshot summary.
- Start and completion timestamps.
- Progress counters.
- Recent events.
- Failure diagnostics.
- Retry history.

### Running jobs

Show current progress using existing Blazor Interactive Server capabilities.

Periodic polling is acceptable.

SignalR-based updates may be used if they fit the existing architecture.

Do not introduce an unnecessary second real-time communication framework.

### UI requirements

- Clear loading states.
- Clear empty states.
- Clear failure states.
- Confirmation for cancellation where appropriate.
- Accessible controls.
- Server-side paging.
- No fabricated progress percentages.
- No raw sensitive exception output.

Do not redesign the application.

## 20. Crawler Scheduling UI

Extend crawler details or editing screens with schedule management.

### Required controls

- Enable/disable schedule.
- Choose schedule type.
- Configure interval or local execution time.
- Select time zone where applicable.
- Display next scheduled execution.
- Save changes.
- Validate the schedule.

### Example

```text
Crawler Schedule

Crawler: Company Documents

Schedule:
(*) Daily
( ) Weekly
( ) Hourly
( ) Disabled

Time:
[ 02:00 ]

Time Zone:
[ Asia/Tokyo ]

Next Run:
2026-10-22 02:00

[ Save Schedule ]
```

The values are illustrative.

Display next-run times computed by the scheduler.

Do not hardcode future execution dates.

### Requirements

- Use existing administration authorization.
- Validate schedules on the server.
- Persist schedules in SQLite.
- Apply changes without restarting Lantern.
- Avoid duplicate scheduled jobs.

## 21. Manual Execution UI Migration

Phase 6 introduced manual crawler execution.

Migrate it to the persistent job system.

### Requirements

- Existing Run actions continue to work.
- Run creates a persistent job.
- UI receives the job identifier.
- Execution continues independently of the browser.
- Job details show the result.
- Duplicate execution remains prevented.
- Disabled crawlers cannot be started.

Remove or adapt obsolete in-memory execution paths.

Do not maintain two separate mechanisms for starting file crawls.

## 22. Security and Authorization

All job management operations are administrator-only.

Protect:

- Manual job submission.
- Schedule creation and modification.
- Job cancellation.
- Job retry.
- Execution history.
- Detailed diagnostics.
- Job retention settings.

### Requirements

- Enforce authorization server-side.
- Validate job and crawler identifiers.
- Prevent unauthorized invocation of background operations.
- Use antiforgery protection for state-changing requests.
- Do not expose secrets in configuration snapshots.
- Do not expose Meilisearch API keys.
- Do not log passwords or sensitive document content.

Avoid allowing untrusted schedule or configuration values to trigger arbitrary filesystem or network operations.

Reuse the existing crawler security boundaries.

## 23. Resource Management

Job scheduling introduces another layer of concurrency.

Coordinate it with existing file processing and embedding limits.

### Requirements

- Bound concurrent crawler jobs.
- Preserve per-crawler execution exclusion.
- Preserve Phase 5 per-file concurrency limits.
- Preserve Phase 3.5 embedding queue limits.
- Avoid overwhelming SQLite.
- Avoid flooding Meilisearch with unlimited indexing operations.
- Avoid unbounded in-memory job queues.

### Configuration hierarchy

A reasonable initial structure is:

```text
MaximumConcurrentJobs = 2
    |
    +-- File Crawler A
    |      MaxConcurrentFiles = 2
    |
    +-- File Crawler B
           MaxConcurrentFiles = 2
```

Keep the system configurable.

Document that overall concurrency depends on both job-level and file-level settings.

Do not implement complex automatic resource scheduling.

## 24. Unit Tests

Add tests covering:

1. Valid job state transitions.
2. Invalid state transition rejection.
3. Job creation.
4. Job claiming.
5. Duplicate claim prevention.
6. Per-crawler execution exclusion.
7. Manual submission validation.
8. Disabled crawler rejection.
9. Schedule calculation.
10. Next-run computation.
11. Time zone conversion.
12. Daylight-saving transitions.
13. Missed schedule coalescing.
14. Cancellation requests.
15. Pending job cancellation.
16. Running job cancellation.
17. Retry creation.
18. Progress aggregation.
19. Retention eligibility.
20. Execution configuration snapshots.

Use deterministic clocks or an injectable `TimeProvider` where practical.

Avoid tests that depend on wall-clock timing.

## 25. SQLite Integration Tests

Use an actual SQLite database.

Test:

1. Migration application.
2. Job persistence.
3. Job state updates.
4. Atomic job claiming.
5. Pending job recovery.
6. Interrupted running-job recovery.
7. Schedule persistence.
8. Schedule changes.
9. Duplicate scheduled occurrence prevention.
10. Progress persistence.
11. Execution history.
12. Retry relationships.
13. Retention cleanup.
14. Concurrent database access.
15. Safe startup recovery.

Use isolated temporary databases.

Do not modify production data.

## 26. End-to-End Execution Tests

Use deterministic local filesystem fixtures.

Test against real crawler services.

### Manual execution

1. Create a crawler.
2. Submit a manual job.
3. Verify persistent `Pending` state.
4. Verify background execution.
5. Verify final status.
6. Verify progress counters.
7. Verify searchable results.

### Scheduled execution

1. Create a recurring schedule.
2. Simulate or advance the scheduler clock.
3. Verify job creation.
4. Verify dispatch.
5. Verify execution.
6. Verify that duplicate occurrences are not created.

### Cancellation

1. Start a long-running test crawl.
2. Request cancellation.
3. Verify safe termination.
4. Verify final job state.
5. Verify no unsafe deletion reconciliation.

### Restart recovery

1. Persist pending and running jobs.
2. Simulate application restart.
3. Verify recovery behavior.
4. Verify interrupted running jobs are identified.
5. Verify pending jobs can resume dispatching.
6. Verify no unintended index deletion.

### Failure and retry

1. Simulate a recoverable extraction or indexing failure.
2. Verify failure diagnostics.
3. Retry the execution.
4. Verify successful synchronization.
5. Verify no duplicate documents or chunks.

### Browser disconnection

Verify that a running crawler continues after the initiating Blazor session disconnects.

## 27. Meilisearch and Hybrid Search Tests

Verify that job completion reflects actual indexing results.

Where a real Meilisearch and embedding server are available, test:

- File crawl to document indexing.
- Chunk indexing.
- Asynchronous Meilisearch task completion.
- Embedding generation.
- Hybrid search retrieval.
- Repeated crawl idempotency.
- Updated document synchronization.
- Deleted-file reconciliation.
- Retry after an indexing failure.

Do not claim successful indexing if Meilisearch tasks remain failed or incomplete.

Do not claim that hybrid integration tests passed unless they were actually executed.

Preserve keyword-only operation when hybrid search is disabled.

## 28. Regression Testing

Run tests from Phases 0–6.

Verify:

- Search UI remains functional.
- Administration login remains functional.
- Crawler configuration CRUD remains functional.
- Path testing remains functional.
- UTF-8 and general text extraction remain correct.
- PDF and Office extraction remain functional.
- Structure-aware chunking remains functional.
- Incremental crawling remains correct.
- Existing Meilisearch integration remains functional.
- Hybrid search remains functional.
- Disabled crawlers do not execute.
- Unsafe deletion reconciliation remains prevented.

Do not change unrelated features merely to simplify job management.

## 29. Performance and Reliability

Measure representative behavior where practical:

- Job submission latency.
- Pending-to-running delay.
- Scheduler dispatch delay.
- Concurrent execution behavior.
- SQLite write frequency.
- Progress update overhead.
- Restart recovery duration.
- History query performance.

Do not invent benchmark results.

Prioritize reliability over high throughput.

### Important operational guarantees

- A submitted job is durably recorded.
- A browser disconnect does not stop execution.
- A process crash does not erase job history.
- A canceled crawl does not delete valid documents accidentally.
- Repeated scheduled occurrences do not create unintended duplicate jobs.
- A failed execution cannot be silently reported as successful.
- Existing synchronized documents remain recoverable after interruption.

## 30. Architecture Guidelines

Suggested logical organization:

```text
Lantern.Core
    |
    +-- CrawlerJob
    +-- CrawlerSchedule
    +-- JobStatus
    +-- JobProgress
    +-- JobResult
    +-- Job service contracts

Lantern.Crawling
    |
    +-- Existing file crawler
    +-- Job dispatcher
    +-- Job execution worker
    +-- Internal scheduler
    +-- Progress reporter
    +-- Cancellation coordinator
    +-- Startup recovery

Lantern.Infrastructure
    |
    +-- SQLite job persistence
    +-- Schedule persistence
    +-- Job events
    +-- EF Core migrations

Lantern.Web
    |
    +-- Admin/Jobs
    +-- Job details
    +-- Schedule configuration
    +-- Manual execution integration
    +-- Progress UI
```

This organization is illustrative.

Adapt to the existing repository.

Avoid unnecessary projects or abstraction layers.

### Extensibility for Phase 8

The execution system should support future crawler types through a small, explicit execution contract.

Do not hardcode file-crawler behavior into the generic job dispatcher.

The dispatcher should resolve an appropriate crawler executor based on the validated crawler type.

For Phase 7, only the file crawler executor needs to be implemented.

Do not create a speculative plugin framework.

## 31. Out of Scope

Do not implement:

- Web crawler execution.
- HTTP page discovery.
- robots.txt handling.
- Web crawler configuration UI.
- Distributed execution.
- Multiple worker nodes.
- External message queues.
- General-purpose workflow engines.
- Advanced cron expression editors.
- Complex automatic retries.
- Priority-based resource scheduling.
- Email or webhook notifications.
- Real-time filesystem watchers.
- Windows Integrated Authentication.
- Windows service installation.
- Cloud-based scheduling.
- RAG or chat features.
- OCR.
- GPU inference.

These belong to later phases or deferred enhancements.

## 32. Acceptance Criteria

Phase 7 is complete only when the following conditions are satisfied.

### Persistent jobs

- Jobs are stored in SQLite.
- Job state transitions are validated.
- Job history survives restart.
- Pending jobs are durably recorded.
- Duplicate execution is prevented.
- Failed jobs contain useful diagnostics.

### Scheduling

- Recurring schedules can be configured.
- Schedules persist across restarts.
- Scheduled jobs execute without an open browser.
- Missed executions follow a documented policy.
- Duplicate scheduled occurrences are prevented.
- Time zone handling is correct.

### Execution management

- Manual runs use persistent jobs.
- Running jobs are independent of Blazor sessions.
- Cancellation is supported.
- Failed executions can be retried.
- Concurrent execution is bounded.
- The same crawler cannot execute concurrently.

### Recovery

- Interrupted running jobs are detected.
- Crash recovery preserves history.
- Pending jobs can be recovered.
- Cancellation intent is preserved.
- Interrupted scans do not cause accidental document deletion.
- Partial indexing can be repaired by later execution.

### Progress and administration

- Job list and details are available.
- Progress counters reflect real crawler activity.
- Execution errors are visible.
- Retry and cancellation controls work.
- Scheduling controls work.
- Administrative authorization is enforced.

### Compatibility

- Existing file crawler behavior is preserved.
- Incremental synchronization remains correct.
- Keyword and hybrid search remain functional.
- UTF-8 and source-code extraction remain functional.
- Crawler configuration UI continues to work.

### Testing

- `dotnet build` succeeds.
- Existing tests pass.
- New unit tests pass.
- SQLite integration tests pass.
- Job recovery tests pass.
- Scheduling tests pass.
- End-to-end execution tests pass where prerequisites are available.

Unverified acceptance criteria must be reported explicitly.

## 33. Deliverables

Provide:

1. Persistent crawler job model.
2. Job state machine.
3. SQLite job schema and migrations.
4. Background job dispatcher.
5. Bounded execution queue.
6. File crawler execution adapter.
7. Manual job submission service.
8. Persistent recurring scheduler.
9. Per-crawler execution exclusion.
10. Cancellation support.
11. Retry support.
12. Startup recovery.
13. Progress reporting.
14. Job history persistence.
15. Execution diagnostics.
16. History and event retention.
17. Jobs administration page.
18. Job detail page.
19. Crawler schedule UI.
20. Updated Phase 6 manual execution integration.
21. Automated tests.
22. Updated documentation.

Document:

- Job lifecycle.
- Job state transitions.
- Manual submission.
- Scheduling configuration.
- Missed execution policy.
- Cancellation behavior.
- Retry behavior.
- Crash recovery.
- Execution ownership.
- Progress reporting.
- Job history retention.
- Current single-instance limitations.

## 34. Required Completion Report

At the end of implementation, provide:

### A. Implementation summary

Describe completed job management functionality.

### B. Files changed

List important created and modified files.

### C. Architectural decisions

Explain:

- Persistent queue strategy.
- Job state model.
- Atomic job claiming.
- Background execution.
- Per-crawler exclusion.
- Schedule representation.
- Missed schedule handling.
- Cancellation.
- Retry semantics.
- Crash recovery.
- Progress persistence.
- Retention.

### D. Build and test commands

List the actual commands executed.

### E. Test results

Clearly distinguish:

- Passed tests.
- Failed tests.
- Skipped tests.
- Tests not executed.

### F. Performance observations

Report measured results where available.

Do not invent performance numbers.

### G. Known limitations

Document limitations including:

- Single-instance execution.
- Cooperative cancellation.
- SQLite concurrency.
- Nontransactional Meilisearch synchronization.
- Scheduler precision.
- Missed execution policy.
- Lack of distributed execution.
- Deferred web crawler integration.

### H. Remaining work

Identify incomplete acceptance criteria and their impact.

Do not begin Phase 8 automatically.

---

## Final Instructions

Implement Phase 7 directly in the existing Lantern repository.

Inspect and preserve all completed functionality from Phases 0–6.

Replace the Phase 6 in-memory manual execution mechanism with a persistent SQLite-backed job system.

Use .NET hosted services for execution and scheduling.

Keep the job dispatcher independent of Blazor circuit lifetime.

Persist job state before acknowledging submission.

Use concurrency-safe state transitions and prevent overlapping execution of the same crawler.

Support manual execution, recurring schedules, cancellation, retry, progress reporting, and job history.

Recover safely from application interruptions without incorrectly deleting indexed documents.

Preserve the existing incremental synchronization, document extraction, chunking, and hybrid search pipelines.

Design the generic job infrastructure so Phase 8 can add a web crawler executor without rewriting scheduling or job management.

Avoid external job frameworks and unnecessary architectural complexity.

**The primary success condition is a reliable persistent crawler execution platform that lets administrators schedule, start, monitor, cancel, and retry file crawler jobs through Lantern's web interface while preserving execution history and data consistency across application restarts and failures.**

Do not implement Phase 8 or later features automatically.
