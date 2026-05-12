namespace SatelliteData.Application.Pipeline;

public interface IPreprocessPipeline
{
    /// <summary>执行预处理阶段；成功后将任务推进到待算法阶段并入队 Algorithm。</summary>
    Task ExecuteAsync(Guid runId, CancellationToken cancellationToken);
}
