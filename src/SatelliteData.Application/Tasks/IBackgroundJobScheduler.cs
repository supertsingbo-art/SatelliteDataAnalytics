namespace SatelliteData.Application.Tasks;

public interface IBackgroundJobScheduler
{
    string EnqueuePreprocess(Guid runId);

    string EnqueueAlgorithm(Guid runId);

    string EnqueueWebhook(Guid runId);
}
