using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

public sealed class TaskListService(
    ITaskRunRepository taskRuns,
    IPreprocessScheduleRepository schedules)
{
    public async Task<IReadOnlyList<TaskListItemDto>> ListAsync(int pageSize, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var cap = Math.Clamp(pageSize, 1, 200);
        var runs = await taskRuns.ListRecentAsync(cap, cancellationToken).ConfigureAwait(false);
        var enabledSchedules = await schedules.ListEnabledAsync(cancellationToken).ConfigureAwait(false);

        var items = new List<TaskListItemDto>();

        foreach (var run in runs.Where(r => r.JobType == TaskJobType.Preprocess))
        {
            items.Add(ToRunItem(run, now));
        }

        foreach (var schedule in enabledSchedules)
        {
            var latest = await taskRuns.GetLatestByScheduleIdAsync(schedule.ScheduleId, cancellationToken)
                .ConfigureAwait(false);
            items.Add(ToScheduleItem(schedule, latest, now));
        }

        return items
            .OrderByDescending(i => i.CreatedAt)
            .Take(cap)
            .ToList();
    }

    public async Task<IReadOnlyList<TaskExecutionRecordDto>> ListExecutionsForRunAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var run = await taskRuns.GetByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            return [];
        }

        var now = DateTimeOffset.UtcNow;
        return [ToExecutionRecord(run, now)];
    }

    public async Task<IReadOnlyList<TaskExecutionRecordDto>> ListExecutionsForScheduleAsync(
        Guid scheduleId,
        CancellationToken cancellationToken)
    {
        var runs = await taskRuns.ListByScheduleIdAsync(scheduleId, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        return runs.Select(r => ToExecutionRecord(r, now)).ToArray();
    }

    private static TaskListItemDto ToRunItem(TaskRun run, DateTimeOffset now) =>
        new(
            ItemType: "RUN",
            ItemId: run.RunId,
            RunId: run.RunId,
            ScheduleId: run.ScheduleId,
            JobId: run.JobId,
            JobType: run.JobType.ToString().ToUpperInvariant(),
            ExecutionMode: PreprocessExecutionModeMapper.ToApi(run.ExecutionMode),
            CanExecute: PreprocessExecutionModeMapper.CanManualExecuteRun(run),
            DisplayStatus: TaskDisplayStatus.ForRun(run, now),
            Status: run.Status.ToString(),
            run.TasookNo,
            run.SatelliteNo,
            run.TestBatchName,
            run.ProgressPercent,
            run.CurrentStep,
            run.ScheduledAt,
            run.CreatedAt,
            run.EndTime);

    private static TaskListItemDto ToScheduleItem(
        PreprocessSchedule schedule,
        TaskRun? latest,
        DateTimeOffset now) =>
        new(
            ItemType: "SCHEDULE",
            ItemId: schedule.ScheduleId,
            RunId: latest?.RunId,
            ScheduleId: schedule.ScheduleId,
            JobId: $"SCH-{schedule.ScheduleId.ToString("N")[..8]}",
            JobType: "PREPROCESS",
            ExecutionMode: "DAILY_RECURRING",
            CanExecute: PreprocessExecutionModeMapper.CanManualExecuteSchedule(schedule),
            DisplayStatus: TaskDisplayStatus.ForSchedule(schedule, latest, now),
            Status: latest?.Status.ToString() ?? "Scheduled",
            schedule.TasookNo,
            schedule.SatelliteNo,
            PreprocessTaskLabels.DailyScheduledDisplayName,
            latest?.ProgressPercent ?? 0m,
            latest?.CurrentStep ?? "schedule",
            null,
            schedule.CreatedAt,
            latest?.EndTime);

    private static TaskExecutionRecordDto ToExecutionRecord(TaskRun run, DateTimeOffset now) =>
        new(
            run.RunId,
            run.JobId,
            run.Status.ToString(),
            TaskDisplayStatus.ForRun(run, now),
            run.StartTime,
            run.EndTime,
            run.WindowStart,
            run.WindowEnd,
            run.ErrorCode,
            run.ErrorMsg);
}
