using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

/// <summary>任务列表用户可见状态。</summary>
public static class TaskDisplayStatus
{
    public const string Pending = "待执行";
    public const string Scheduled = "任务定时中";
    public const string Running = "任务执行中";
    public const string Finished = "任务执行完毕";

    public static string ForRun(TaskRun run, DateTimeOffset nowUtc)
    {
        if (run.Status == TaskRunStatus.Running)
        {
            return Running;
        }

        if (run.Status is TaskRunStatus.Succeeded or TaskRunStatus.Failed or TaskRunStatus.Timeout
            or TaskRunStatus.Cancelled)
        {
            return Finished;
        }

        if (run.ExecutionMode == PreprocessExecutionMode.OnceScheduled
            && run.ScheduledAt is { } at
            && at > nowUtc
            && run.Status == TaskRunStatus.Queued)
        {
            return Scheduled;
        }

        if (PreprocessExecutionModeMapper.IsImmediatePending(run))
        {
            return Pending;
        }

        if (run.ExecutionMode == PreprocessExecutionMode.OnceScheduled
            && run.Status == TaskRunStatus.Queued)
        {
            return Scheduled;
        }

        return run.Status.ToString();
    }

    public static string ForSchedule(PreprocessSchedule schedule, TaskRun? latestInstance, DateTimeOffset nowUtc)
    {
        if (!schedule.Enabled)
        {
            return Finished;
        }

        if (latestInstance?.Status == TaskRunStatus.Running)
        {
            return Running;
        }

        if (latestInstance is { Status: TaskRunStatus.Succeeded or TaskRunStatus.Failed or TaskRunStatus.Timeout })
        {
            return Scheduled;
        }

        return Scheduled;
    }
}
