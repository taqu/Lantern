# Lantern — Fix Scheduler Query Failure and Infinite Error Loop

## Objective

Fix two related issues in Lantern's crawler scheduler:

1. EF Core cannot translate the due-schedule query into SQLite-compatible SQL.
2. The scheduler repeatedly executes the failing query, producing an endless stream of identical exceptions.

The scheduler must not retry deterministic programming or query translation errors indefinitely.

The application must remain usable when the scheduler encounters a fatal internal error.

## 1. Investigate the Existing Implementation

Inspect:

- `CrawlerScheduleRepository.cs`
- `CrawlerScheduleRecord`
- `CrawlerSchedulerService.cs`
- The EF Core DbContext and entity mappings
- Schedule creation and update logic
- SQLite migrations
- Background service registration
- Existing scheduler tests

Identify the exact data types used by `NextRunAt` and `now`.

The current failing expression contains:

```csharp
(DateTimeOffset?)c.NextRunAt <= @now
```

Determine why an implicit `DateTimeOffset` conversion is present.

## 2. Fix SQLite Query Translation

The due-schedule query must execute entirely on SQLite.

Preferred approach:

- Store executable schedule timestamps as UTC `DateTime`.
- Convert incoming `DateTimeOffset` values to UTC `DateTime` before constructing the query.
- Compare compatible types.
- Avoid `DateTimeOffset` conversions inside LINQ expressions.
- Keep ordering and filtering server-side.

Do not fix this by loading all schedules into memory.

Check other repository queries for similar SQLite-incompatible comparisons.

If changing persisted types, implement a safe EF Core migration that preserves existing timestamps and their actual instants.

## 3. Classify Scheduler Failures

Introduce explicit handling for:

**Cancellation**

Exit normally when the application is shutting down.

**Transient infrastructure failures**

Retry with bounded exponential backoff.

Only classify errors as transient when there is evidence that retrying may succeed.

**Permanent errors**

Stop the scheduler immediately for:

- LINQ translation failures.
- Unsupported query operations.
- Invalid persistent schema.
- Deterministic configuration errors.
- Other clearly nonrecoverable programming defects.

**Unknown errors**

Apply a limited retry policy, then transition the scheduler to a faulted state.

Never retry unknown failures indefinitely.

## 4. Prevent Infinite Error Logging

The scheduler must not repeatedly log the same deterministic failure on every polling interval.

Requirements:

- Log the original fatal error once.
- Record the scheduler fault state.
- Exit the scheduler execution loop.
- Avoid repeated identical stack traces.
- Preserve enough diagnostics to investigate the failure.

Do not swallow exceptions and continue polling.

Do not implement an unbounded `catch (Exception)` retry loop.

## 5. Preserve Application Availability

A fatal scheduler error should not automatically stop the Lantern web application.

When the scheduler enters a faulted state:

- Stop scheduled job dispatching.
- Preserve pending jobs.
- Preserve running job ownership and recovery semantics.
- Keep search and administration functionality available.
- Expose the scheduler fault through the existing System Status interface.
- Provide an actionable diagnostic message.

Do not globally configure `BackgroundServiceExceptionBehavior.Ignore`.

Handle expected scheduler failures within the scheduler's own lifecycle.

If a fault is critical to overall data safety, document whether a full application shutdown is necessary instead of silently continuing.

## 6. Implement Bounded Retry

For transient failures, implement a simple bounded exponential backoff.

Suggested policy:

```text
Attempt 1: 1 second
Attempt 2: 2 seconds
Attempt 3: 4 seconds
Attempt 4: 8 seconds
Attempt 5: 16 seconds

Maximum delay: 30 seconds
Maximum consecutive failures: 5
```

These are illustrative defaults.

Reset the consecutive failure counter after a successful scheduler check.

After the configured failure limit, stop the scheduler and report `Faulted`.

Do not let the retry mechanism create overlapping scheduler checks.

## 7. Cancellation Handling

Handle cancellation separately:

```csharp
catch (OperationCanceledException)
    when (stoppingToken.IsCancellationRequested)
{
    // Normal application shutdown.
    return;
}
```

Adapt this to the existing execution structure.

Do not log expected shutdown cancellation as an error.

Do not classify arbitrary `OperationCanceledException` instances as normal shutdown unless the relevant cancellation token was actually requested.

## 8. Scheduler Health State

Reuse existing health/status infrastructure.

Expose states such as:

```text
Starting
Running
Faulted
Stopped
```

Requirements:

- `Running` means scheduler checks are functioning.
- `Faulted` means scheduling has stopped because of an error.
- `Stopped` means controlled shutdown.
- Preserve the last meaningful error.
- Make the state visible to administrators.
- Do not report successful scheduling while the service is faulted.

An in-memory health state is acceptable for this correction if the existing architecture does not require persistent service health.

## 9. Preserve Job Semantics

The fix must not change:

- Job identity.
- Schedule identity.
- Pending job persistence.
- Per-crawler execution exclusion.
- Retry relationships.
- Missed-schedule handling.
- Time zone calculation.
- Crawler execution history.

Ensure an interrupted scheduler does not fabricate new jobs or lose existing pending jobs.

Do not delete schedules as an error recovery shortcut.

## 10. Tests

Add tests covering:

1. SQLite translation of `GetDueSchedulesAsync`.
2. UTC timestamp comparison.
3. Due schedules.
4. Future schedules.
5. Disabled schedules.
6. Null next-run timestamps.
7. UTC boundary conditions.
8. Existing persisted schedule values.
9. Deterministic query failure.
10. Scheduler stopping after permanent failure.
11. No repeated logging of permanent failures.
12. Transient SQLite busy errors.
13. Exponential backoff limits.
14. Retry counter reset.
15. Unknown exception retry exhaustion.
16. Normal cancellation.
17. Scheduler fault status.
18. Web application availability after scheduler fault.

Use real SQLite integration tests for query translation.

Do not rely on EF Core's in-memory provider.

Use controlled time through the existing `TimeProvider` abstraction where practical.

## 11. Acceptance Criteria

The fix is complete when:

- The due-schedule query executes successfully against SQLite.
- No implicit `DateTimeOffset` conversion remains in the SQL-translated predicate.
- Existing schedule timestamps remain correct.
- Due schedules are returned accurately.
- Permanent errors stop the scheduler.
- Permanent errors are not logged repeatedly.
- Transient failures use bounded retries.
- Retry exhaustion transitions the scheduler to `Faulted`.
- Normal cancellation does not produce error logs.
- The web application remains available after an isolated scheduler fault.
- Existing crawler jobs and schedules remain intact.
- Scheduler status accurately reports failures.
- Automated tests pass.

## 12. Completion Report

Report:

1. Root cause of the EF Core translation failure.
2. Root cause of the repeated exception loop.
3. Files modified.
4. Database migration changes, if any.
5. Final exception classification policy.
6. Retry and backoff behavior.
7. Scheduler fault reporting.
8. Build and test commands executed.
9. Actual test results.
10. Remaining limitations.

## Final Instructions

Fix both the SQLite query translation failure and the scheduler's infinite error loop.

Do not treat deterministic programming errors as transient infrastructure failures.

Use UTC-compatible database queries, preserve existing data, and verify with real SQLite tests.

Stop the scheduler after a permanent error instead of retrying indefinitely.

Keep Lantern's search and administration functions operational when the scheduler alone fails.

**The primary success condition is that a scheduler query failure produces one actionable diagnostic and a clearly reported faulted state, rather than an endless sequence of identical exceptions.**
