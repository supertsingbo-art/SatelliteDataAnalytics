using SatelliteData.Application.Pipeline;
using SatelliteData.Application.Tasks;
using SatelliteData.Domain.Tasks;

namespace SatelliteData.Infrastructure.Pipeline;

public sealed class InMemoryTaskRunRepository : ITaskRunRepository
{
    private readonly Dictionary<Guid, TaskRun> _byId = [];
    private readonly Dictionary<string, TaskRun> _byIdem = [];
    private readonly object _gate = new();

    public Task<TaskRun?> GetByRunIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _byId.TryGetValue(runId, out var r);
            return Task.FromResult(r);
        }
    }

    public Task<TaskRun?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _byIdem.TryGetValue(idempotencyKey, out var r);
            return Task.FromResult(r);
        }
    }

    public Task InsertAsync(TaskRun run, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _byId[run.RunId] = run;
            _byIdem[run.IdempotencyKey] = run;
            return Task.CompletedTask;
        }
    }

    public Task UpdateAsync(TaskRun run, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _byId[run.RunId] = run;
            if (_byIdem.TryGetValue(run.IdempotencyKey, out var existing) && existing.RunId == run.RunId)
            {
                _byIdem[run.IdempotencyKey] = run;
            }

            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<TaskRun>> ListRecentAsync(int limit, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            var cap = Math.Clamp(limit, 1, 200);
            var arr = _byId.Values
                .OrderByDescending(r => r.CreatedAt)
                .Take(cap)
                .ToArray();
            return Task.FromResult<IReadOnlyList<TaskRun>>(arr);
        }
    }
}

public sealed class InMemoryTaskEventRepository : ITaskEventRepository
{
    private readonly List<TaskEvent> _events = [];
    private readonly object _gate = new();

    public Task AppendAsync(TaskEvent evt, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _events.Add(evt);
            return Task.CompletedTask;
        }
    }
}

public sealed class InMemoryHqParamMetadataRepository : IHqParamMetadataRepository
{
    public Task InsertAsync(HqParamMetadataRow row, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class InMemoryClientCallbackRepository : IClientCallbackRepository
{
    public Task<IReadOnlyList<ClientCallbackRow>> GetEnabledCallbacksAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ClientCallbackRow>>(Array.Empty<ClientCallbackRow>());

    public Task InsertDeliveryAsync(
        Guid deliveryId,
        string eventId,
        Guid callbackId,
        Guid? runId,
        string eventType,
        string payloadJson,
        string status,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public Task UpdateDeliveryAsync(
        Guid deliveryId,
        string status,
        int responseStatus,
        string? responseBody,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
