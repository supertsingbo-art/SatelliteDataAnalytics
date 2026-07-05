using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

public sealed class TaskListService(
    ITaskRunRepository taskRuns,
    IPreprocessScheduleRepository schedules,
    PreprocessConflictReader conflictReader)
{
    public async Task<IReadOnlyList<TaskListItemDto>> ListAsync(int pageSize, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var cap = Math.Clamp(pageSize, 1, 200);
        var runs = await taskRuns.ListRecentAsync(cap, cancellationToken).ConfigureAwait(false);
        var enabledSchedules = await schedules.ListEnabledAsync(cancellationToken).ConfigureAwait(false);

        var items = new List<TaskListItemDto>();

        foreach (var run in runs.Where(r => r.JobType is TaskJobType.Preprocess or TaskJobType.Pipeline))
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
        return [await ToExecutionRecordAsync(run, now, cancellationToken).ConfigureAwait(false)];
    }

    public async Task<IReadOnlyList<TaskExecutionRecordDto>> ListExecutionsForScheduleAsync(
        Guid scheduleId,
        CancellationToken cancellationToken)
    {
        var runs = await taskRuns.ListByScheduleIdAsync(scheduleId, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var records = new List<TaskExecutionRecordDto>();
        foreach (var run in runs)
        {
            records.Add(await ToExecutionRecordAsync(run, now, cancellationToken).ConfigureAwait(false));
        }

        return records;
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
            CanDelete: TaskRunStateHelper.CanDeleteRun(run),
            CanReExecute: TaskRunStateHelper.CanReExecuteRun(run),
            CanViewData: TaskRunStateHelper.CanViewProcessedData(run),
            CanViewAlgorithmResults: TaskRunStateHelper.CanViewAlgorithmResults(run),
            OutlierPendingCount: run.OutlierPendingCount,
            OutlierReviewStatus: run.OutlierReviewStatus,
            StatusSummary: TaskRunStateHelper.BuildStatusSummary(run, TaskDisplayStatus.ForRun(run, now)),
            DisplayStatus: TaskDisplayStatus.ForRun(run, now),
            Status: run.Status.ToString(),
            run.TasookNo,
            run.SatelliteNo,
            run.TestBatchName,
            run.ProgressPercent,
            run.CurrentStep,
            run.ScheduledAt,
            run.CreatedAt,
            run.EndTime,
            run.ErrorCode,
            run.ErrorMsg,
            PipelineUsesFilterTemplate: run.JobType == TaskJobType.Pipeline && run.FilterTemplateId is not null);

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
            CanDelete: false,
            CanReExecute: false,
            CanViewData: latest is not null && TaskRunStateHelper.CanViewProcessedData(latest),
            CanViewAlgorithmResults: latest is not null && TaskRunStateHelper.CanViewAlgorithmResults(latest),
            OutlierPendingCount: latest?.OutlierPendingCount ?? 0,
            OutlierReviewStatus: latest?.OutlierReviewStatus,
            StatusSummary: latest is not null
                ? TaskRunStateHelper.BuildStatusSummary(latest, TaskDisplayStatus.ForSchedule(schedule, latest, now))
                : TaskDisplayStatus.ForSchedule(schedule, latest, now),
            DisplayStatus: TaskDisplayStatus.ForSchedule(schedule, latest, now),
            Status: latest?.Status.ToString() ?? "Scheduled",
            schedule.TasookNo,
            schedule.SatelliteNo,
            PreprocessTaskLabels.DailyScheduledDisplayName,
            latest?.ProgressPercent ?? 0m,
            latest?.CurrentStep ?? "schedule",
            null,
            schedule.CreatedAt,
            latest?.EndTime,
            latest?.ErrorCode,
            latest?.ErrorMsg,
            PipelineUsesFilterTemplate: false);

    private async Task<TaskExecutionRecordDto> ToExecutionRecordAsync(
        TaskRun run,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var conflictDetails = await conflictReader.TryGetConflictDetailsAsync(run, cancellationToken)
            .ConfigureAwait(false);
        return new TaskExecutionRecordDto(
            run.RunId,
            run.JobId,
            run.Status.ToString(),
            TaskDisplayStatus.ForRun(run, now),
            run.StartTime,
            run.EndTime,
            run.WindowStart,
            run.WindowEnd,
            run.ErrorCode,
            run.ErrorMsg,
            conflictDetails);
    }
}
