namespace SatelliteData.Application.Pipeline;

public interface IAlgorithmExecutionPipeline
{
    Task ExecuteAsync(Guid runId, CancellationToken cancellationToken);
}
