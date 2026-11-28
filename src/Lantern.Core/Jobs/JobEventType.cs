namespace Lantern.Core.Jobs;

public enum JobEventType
{
    JobCreated,
    JobStarted,
    ProgressUpdated,
    JobCompleted,
    JobFailed,
    CancellationRequested,
    JobCanceled,
    JobInterrupted,
    JobRetried
}
