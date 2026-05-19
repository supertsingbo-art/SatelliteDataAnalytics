namespace SatelliteData.Application.Tasks;

public interface IBackgroundJobScheduler
{
    string EnqueuePreprocess(Guid runId);

    string SchedulePreprocess(Guid runId, DateTimeOffset runAt);

    void AddOrUpdateDailySchedule(Guid scheduleId, string cronExpression);

    void RemoveDailySchedule(string recurringJobId);

    bool TryDeleteScheduledJob(string hangfireJobId);

    string EnqueueAlgorithm(Guid runId);

    string EnqueueWebhook(Guid runId);
}
