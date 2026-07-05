using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

public static class TaskRunStateHelper
{
    public static bool IsTerminal(TaskRunStatus status) =>
        status is TaskRunStatus.Succeeded
            or TaskRunStatus.Failed
            or TaskRunStatus.Timeout
            or TaskRunStatus.Cancelled;

    public static bool CanDeleteRun(TaskRun run) =>
        run.JobType == TaskJobType.Preprocess && IsTerminal(run.Status);

    public static bool CanReExecuteRun(TaskRun run) =>
        IsTerminal(run.Status)
        && run.ExecutionMode is null or PreprocessExecutionMode.Immediate
        && (
            run.JobType == TaskJobType.Preprocess
            || (run.JobType == TaskJobType.Pipeline && run.FilterTemplateId is not null));

    public static bool CanViewProcessedData(TaskRun run) =>
        run.Status == TaskRunStatus.Succeeded
        && (
            run.JobType == TaskJobType.Preprocess
            || (run.JobType == TaskJobType.Pipeline && run.FilterTemplateId is not null));

    public static bool CanViewAlgorithmResults(TaskRun run) =>
        run.Status == TaskRunStatus.Succeeded
        && run.AlgorithmTemplateId is not null;

    /// <summary>合并展示状态、状态、当前步骤；取消任务显示 cancelled。</summary>
    public static string BuildStatusSummary(TaskRun run, string displayStatus)
    {
        if (run.Status == TaskRunStatus.Cancelled)
        {
            return "cancelled";
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(displayStatus))
        {
            parts.Add(displayStatus);
        }

        parts.Add(run.Status.ToString());

        if (!string.IsNullOrWhiteSpace(run.CurrentStep)
            && !string.Equals(run.CurrentStep, run.Status.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(run.CurrentStep);
        }

        return string.Join(" · ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
