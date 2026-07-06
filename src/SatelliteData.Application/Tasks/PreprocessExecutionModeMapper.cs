using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

public static class PreprocessExecutionModeMapper
{
    public static string? ToApi(PreprocessExecutionMode? mode) =>
        mode switch
        {
            PreprocessExecutionMode.OnceScheduled => "ONCE_SCHEDULED",
            PreprocessExecutionMode.DailyInstance => "DAILY_INSTANCE",
            PreprocessExecutionMode.Immediate => "IMMEDIATE",
            _ => null
        };

    /// <summary>是否为「创建后待手动执行」的立即任务（含历史数据 execution_mode 为空）。</summary>
    public static bool IsImmediatePending(TaskRun run) =>
        run.Status == TaskRunStatus.Queued
        && string.IsNullOrWhiteSpace(run.HangfireJobId)
        && run.ExecutionMode is null or PreprocessExecutionMode.Immediate;

    public static bool CanManualExecuteRun(TaskRun run) =>
        run.JobType == TaskJobType.Preprocess
        && run.Status == TaskRunStatus.Queued
        && run.ExecutionMode is null or PreprocessExecutionMode.Immediate;

    public static bool CanManualExecuteSchedule(PreprocessSchedule schedule) => schedule.Enabled;
}
