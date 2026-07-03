using System.Collections.Concurrent;

namespace SatelliteData.Application.Tasks;

public sealed class InMemoryTaskRunConflictOptionStore : ITaskRunConflictOptionStore
{
    private readonly ConcurrentDictionary<Guid, PreprocessConflictHandlingOptions> _byRun = new();

    public void Set(Guid runId, PreprocessConflictHandlingOptions options)
    {
        _byRun[runId] = options;
    }

    public bool TryGet(Guid runId, out PreprocessConflictHandlingOptions options)
    {
        return _byRun.TryGetValue(runId, out options!);
    }

    public void Clear(Guid runId)
    {
        _ = _byRun.TryRemove(runId, out _);
    }
}
