namespace Lantern.Core.Jobs;

public enum SchedulerState { Starting, Running, Faulted, Stopped }

public sealed record SchedulerStatus(SchedulerState State, string? LastError, DateTimeOffset? FaultedAt);

public interface ISchedulerHealthService
{
    SchedulerStatus GetStatus();
}
