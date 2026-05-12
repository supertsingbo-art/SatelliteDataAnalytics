using Hangfire;
using SatelliteData.Application.Tasks;

namespace SatelliteData.Infrastructure.Pipeline;

public sealed class HangfireBackgroundJobScheduler : IBackgroundJobScheduler
{
    public string EnqueuePreprocess(Guid runId) =>
        BackgroundJob.Enqueue<PipelineJobDispatcher>(d => d.RunPreprocess(runId));

    public string EnqueueAlgorithm(Guid runId) =>
        BackgroundJob.Enqueue<PipelineJobDispatcher>(d => d.RunAlgorithm(runId));

    public string EnqueueWebhook(Guid runId) =>
        BackgroundJob.Enqueue<PipelineJobDispatcher>(d => d.RunWebhook(runId));
}
