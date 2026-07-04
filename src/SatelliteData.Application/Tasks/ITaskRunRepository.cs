using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

public interface ITaskRunRepository
{
    Task<TaskRun?> GetByRunIdAsync(Guid runId, CancellationToken cancellationToken);

    Task<TaskRun?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    Task InsertAsync(TaskRun run, CancellationToken cancellationToken);

    Task UpdateAsync(TaskRun run, CancellationToken cancellationToken);

    /// <summary>更新任务状态，若已为 Cancelled 则不覆盖（返回 false）。</summary>
    Task<bool> UpdateIfNotCancelledAsync(TaskRun run, CancellationToken cancellationToken);

    /// <summary>按创建时间倒序返回最近任务（用于管理端列表）。</summary>
    Task<IReadOnlyList<TaskRun>> ListRecentAsync(int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<TaskRun>> ListByFilterTemplateIdAsync(Guid filterTemplateId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TaskRun>> ListByAlgorithmTemplateIdAsync(Guid algorithmTemplateId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TaskRun>> ListByScheduleIdAsync(Guid scheduleId, CancellationToken cancellationToken);

    Task<TaskRun?> GetLatestByScheduleIdAsync(Guid scheduleId, CancellationToken cancellationToken);

    Task DeleteAsync(Guid runId, CancellationToken cancellationToken);
}
