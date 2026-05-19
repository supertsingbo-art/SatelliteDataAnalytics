using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

/// <summary>流水线执行过程中协作式检测用户取消。</summary>
public static class TaskRunCancellation
{
    public static async Task<bool> IsCancelledAsync(
        ITaskRunRepository taskRuns,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var latest = await taskRuns.GetByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        return latest?.Status == TaskRunStatus.Cancelled;
    }
}
