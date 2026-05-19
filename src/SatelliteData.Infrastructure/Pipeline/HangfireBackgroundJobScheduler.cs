using Hangfire;
using SatelliteData.Application.Tasks;

namespace SatelliteData.Infrastructure.Pipeline;

public sealed class HangfireBackgroundJobScheduler : IBackgroundJobScheduler
{
    public string EnqueuePreprocess(Guid runId) =>
        BackgroundJob.Enqueue<PipelineJobDispatcher>(d => d.RunPreprocess(runId));

    public string SchedulePreprocess(Guid runId, DateTimeOffset runAt) =>
        BackgroundJob.Schedule<PipelineJobDispatcher>(d => d.RunPreprocess(runId), runAt);

    public void AddOrUpdateDailySchedule(Guid scheduleId, string cronExpression)
    {
        var recurringId = PreprocessWindowCalculator.BuildRecurringJobId(scheduleId);
        RecurringJob.AddOrUpdate<PipelineJobDispatcher>(
            recurringId,
            d => d.RunDailyPreprocessSchedule(scheduleId),
            cronExpression);
    }

    public void RemoveDailySchedule(string recurringJobId) =>
        RecurringJob.RemoveIfExists(recurringJobId);

    public bool TryDeleteScheduledJob(string hangfireJobId)
    {
        if (string.IsNullOrWhiteSpace(hangfireJobId))
        {
            return false;
        }

        return BackgroundJob.Delete(hangfireJobId);
    }

    public string EnqueueAlgorithm(Guid runId) =>
        BackgroundJob.Enqueue<PipelineJobDispatcher>(d => d.RunAlgorithm(runId));

    public string EnqueueWebhook(Guid runId) =>
        BackgroundJob.Enqueue<PipelineJobDispatcher>(d => d.RunWebhook(runId));
}
