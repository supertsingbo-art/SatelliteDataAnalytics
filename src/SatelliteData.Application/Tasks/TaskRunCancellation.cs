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
        cancellationToken.ThrowIfCancellationRequested();
        var latest = await taskRuns.GetByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        return latest?.Status == TaskRunStatus.Cancelled;
    }

    /// <summary>若已取消（token 或 DB）则抛出 <see cref="OperationCanceledException"/>。</summary>
    public static async Task ThrowIfCancelledAsync(
        ITaskRunRepository taskRuns,
        Guid runId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (await IsCancelledAsync(taskRuns, runId, cancellationToken).ConfigureAwait(false))
        {
            throw new OperationCanceledException($"Task run {runId} was cancelled.");
        }
    }
}
