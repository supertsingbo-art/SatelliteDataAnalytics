using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

public interface ITaskEventRepository
{
    Task AppendAsync(TaskEvent evt, CancellationToken cancellationToken);

    Task DeleteByRunIdAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>获取指定 run 最近一次 task.failed 事件（含 PRE_006 payload）。</summary>
    Task<TaskEvent?> GetLatestFailedEventAsync(Guid runId, CancellationToken cancellationToken);
}
