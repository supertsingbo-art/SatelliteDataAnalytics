using System.Collections.Concurrent;

namespace SatelliteData.Application.Tasks;

public sealed class TaskRunCancellationRegistry : ITaskRunCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _sources = new();

    public TaskRunCancellationRegistration Register(Guid runId)
    {
        var cts = new CancellationTokenSource();
        if (_sources.TryGetValue(runId, out var existing))
        {
            existing.Dispose();
        }

        _sources[runId] = cts;
        return new TaskRunCancellationRegistration(this, runId, cts);
    }

    public void Cancel(Guid runId)
    {
        if (_sources.TryGetValue(runId, out var cts))
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 任务已结束并释放
            }
        }
    }

    internal void Unregister(Guid runId, CancellationTokenSource expected)
    {
        if (_sources.TryGetValue(runId, out var existing) && ReferenceEquals(existing, expected))
        {
            _sources.TryRemove(runId, out _);
        }
    }
}
