namespace SatelliteData.Application.Tasks;

/// <summary>任务运行期取消信号注册表（内存 CTS，与 DB status=Cancelled 配合）。</summary>
public interface ITaskRunCancellationRegistry
{
    /// <summary>为 run 注册 CTS，返回可释放的句柄及其 CancellationToken。</summary>
    TaskRunCancellationRegistration Register(Guid runId);

    /// <summary>用户取消时触发信号（若 run 未注册则忽略）。</summary>
    void Cancel(Guid runId);
}

/// <summary><see cref="ITaskRunCancellationRegistry.Register"/> 返回的注册句柄。</summary>
public sealed class TaskRunCancellationRegistration : IDisposable
{
    private readonly ITaskRunCancellationRegistry _registry;
    private readonly Guid _runId;
    private int _disposed;

    internal TaskRunCancellationRegistration(
        ITaskRunCancellationRegistry registry,
        Guid runId,
        CancellationTokenSource cts)
    {
        _registry = registry;
        _runId = runId;
        Token = cts.Token;
        Cts = cts;
    }

    internal CancellationTokenSource Cts { get; }

    public CancellationToken Token { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_registry is TaskRunCancellationRegistry concrete)
        {
            concrete.Unregister(_runId, Cts);
        }

        Cts.Dispose();
    }
}
