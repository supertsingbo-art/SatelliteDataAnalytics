using Microsoft.Extensions.Logging;
using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

public sealed class TaskExecutionService(
    ITaskRunRepository taskRuns,
    IPreprocessScheduleRepository schedules,
    IBackgroundJobScheduler scheduler,
    ITaskRunConflictOptionStore conflictOptionStore,
    ILogger<TaskExecutionService> logger)
{
    public async Task<ExecuteTaskResultDto> ExecuteRunAsync(
        Guid runId,
        PreprocessConflictHandlingOptions? conflictOptions,
        CancellationToken cancellationToken)
    {
        var run = await taskRuns.GetByRunIdAsync(runId, cancellationToken).ConfigureAwait(false)
            ?? throw new TaskValidationException(TaskErrorCodes.NotFound, "任务不存在");

        if (run.JobType != TaskJobType.Preprocess)
        {
            throw new TaskValidationException(TaskErrorCodes.NotCancellable, "仅预处理任务支持手动执行");
        }

        var now = DateTimeOffset.UtcNow;
        var isImmediateMode = run.ExecutionMode is null or PreprocessExecutionMode.Immediate;

        if (isImmediateMode)
        {
            if (run.Status == TaskRunStatus.Running && !string.IsNullOrWhiteSpace(run.HangfireJobId))
            {
                return ToResult(run, now);
            }

            if (run.Status != TaskRunStatus.Queued)
            {
                throw new TaskValidationException(TaskErrorCodes.NotCancellable, "任务已启动或已结束，无法重复执行");
            }

            var hangfireId = scheduler.EnqueuePreprocess(runId);
            if (conflictOptions is null)
            {
                conflictOptionStore.Clear(runId);
            }
            else
            {
                conflictOptionStore.Set(runId, conflictOptions);
            }
            run = run with
            {
                HangfireJobId = hangfireId,
                CurrentStep = "queued",
                ExecutionMode = run.ExecutionMode ?? PreprocessExecutionMode.Immediate
            };
            await taskRuns.UpdateAsync(run, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Manual execute preprocess {RunId}, Hangfire {JobId}", runId, hangfireId);
            return ToResult(run, now);
        }

        if (run.ExecutionMode == PreprocessExecutionMode.OnceScheduled)
        {
            return ToResult(run, now);
        }

        throw new TaskValidationException(TaskErrorCodes.ExecutionModeInvalid, "该任务类型请使用计划执行接口");
    }

    public async Task<ExecuteTaskResultDto> ExecuteScheduleAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        var schedule = await schedules.GetByIdAsync(scheduleId, cancellationToken).ConfigureAwait(false)
            ?? throw new TaskValidationException(TaskErrorCodes.NotFound, "定时计划不存在");

        if (!schedule.Enabled)
        {
            var cron = PreprocessWindowCalculator.BuildDailyCron(schedule.DailyTime);
            scheduler.AddOrUpdateDailySchedule(scheduleId, cron);
            schedule = schedule with { Enabled = true, UpdatedAt = DateTimeOffset.UtcNow };
            await schedules.UpdateAsync(schedule, cancellationToken).ConfigureAwait(false);
        }

        var latest = await taskRuns.GetLatestByScheduleIdAsync(scheduleId, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var display = TaskDisplayStatus.ForSchedule(schedule, latest, now);
        logger.LogInformation("Schedule {ScheduleId} enabled/confirmed, display={Display}", scheduleId, display);
        return new ExecuteTaskResultDto(display, latest?.RunId, scheduleId, latest?.JobId, latest?.Status.ToString() ?? "Scheduled");
    }

    private static ExecuteTaskResultDto ToResult(TaskRun run, DateTimeOffset now) =>
        new(
            TaskDisplayStatus.ForRun(run, now),
            run.RunId,
            run.ScheduleId,
            run.JobId,
            run.Status.ToString());
}
