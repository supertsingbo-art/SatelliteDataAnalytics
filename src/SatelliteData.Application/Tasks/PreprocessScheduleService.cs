using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SatelliteData.Domain.Tasks;
using static SatelliteData.Application.Tasks.PreprocessTaskLabels;

namespace SatelliteData.Application.Tasks;

public sealed class PreprocessScheduleService(
    IPreprocessScheduleRepository schedules,
    ITaskRunRepository taskRuns,
    ITaskEventRepository taskEvents,
    IBackgroundJobScheduler scheduler,
    PreprocessTaskValidator validator,
    ILogger<PreprocessScheduleService> logger)
{
    public async Task<PreprocessScheduleCreateResult> CreateDailyScheduleAsync(
        PreprocessCreateCommand command,
        Guid? createdBy,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAsync(command, cancellationToken);

        var scheduleId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var recurringId = PreprocessWindowCalculator.BuildRecurringJobId(scheduleId);
        var dailyTime = command.DailyTime!.Value;
        var effectiveFrom = command.EffectiveFrom!.Value;
        var intervalDays = command.IntervalDays ?? 1;

        var schedule = new PreprocessSchedule(
            scheduleId,
            command.TasookNo.Trim(),
            command.SatelliteNo.Trim(),
            command.FilterTemplateId,
            command.FilterTemplateVersion,
            dailyTime,
            intervalDays,
            effectiveFrom,
            Enabled: true,
            recurringId,
            LastRunId: null,
            LastRunStatus: null,
            LastRunEndAt: null,
            now,
            now);

        await schedules.InsertAsync(schedule, cancellationToken);

        var cron = PreprocessWindowCalculator.BuildDailyCron(dailyTime);
        scheduler.AddOrUpdateDailySchedule(scheduleId, cron);

        await taskEvents.AppendAsync(
            new TaskEvent(
                Guid.NewGuid(),
                Guid.Empty,
                "preprocess.schedule.created",
                "Succeeded",
                JsonSerializer.Serialize(new { scheduleId, command.TasookNo, command.SatelliteNo }),
                null,
                null,
                now),
            cancellationToken);

        logger.LogInformation(
            "Daily preprocess schedule {ScheduleId} registered cron={Cron}",
            scheduleId,
            cron);

        return new PreprocessScheduleCreateResult(scheduleId, recurringId);
    }

    public async Task TriggerInstanceAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        var schedule = await schedules.GetByIdAsync(scheduleId, cancellationToken);
        if (schedule is null || !schedule.Enabled)
        {
            return;
        }

        var todayLocal = DateOnly.FromDateTime(DateTime.Now);
        if (!PreprocessWindowCalculator.ShouldRunToday(schedule.EffectiveFrom, schedule.IntervalDays, todayLocal))
        {
            logger.LogDebug("Skipping schedule {ScheduleId} — not a run day", scheduleId);
            return;
        }

        var (windowStart, windowEnd) = PreprocessWindowCalculator.ComputeDailyWindow(todayLocal, schedule.DailyTime);
        var now = DateTimeOffset.UtcNow;
        var runId = Guid.NewGuid();
        var jobId = $"PRE-{DateTime.UtcNow:yyyyMMddHHmmss}-{runId.ToString("N")[..8]}";
        var idempotencyKey = BuildDailyInstanceKey(scheduleId, todayLocal);

        var existing = await taskRuns.GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            logger.LogDebug("Daily instance already exists for schedule {ScheduleId} on {Date}", scheduleId, todayLocal);
            return;
        }

        var run = new TaskRun(
            RunId: runId,
            ParentRunId: null,
            JobId: jobId,
            JobType: TaskJobType.Preprocess,
            TriggerType: TaskTriggerType.Scheduled,
            Status: TaskRunStatus.Queued,
            IdempotencyKey: idempotencyKey,
            TasookNo: schedule.TasookNo,
            SatelliteNo: schedule.SatelliteNo,
            TestBatchName: DailyScheduledDisplayName,
            WindowStart: windowStart,
            WindowEnd: windowEnd,
            FilterTemplateId: schedule.FilterTemplateId,
            FilterTemplateVersion: schedule.FilterTemplateVersion,
            AlgorithmTemplateId: null,
            AlgorithmTemplateVersion: null,
            ReportTemplateId: null,
            ReportTemplateVersion: null,
            ProgressPercent: 3m,
            CurrentStep: "Queued",
            StartTime: null,
            EndTime: null,
            TimeoutFlag: false,
            ErrorCode: null,
            ErrorMsg: null,
            CreatedBy: null,
            CreatedAt: now,
            ExecutionMode: PreprocessExecutionMode.DailyInstance,
            ScheduledAt: windowEnd,
            ScheduleId: scheduleId,
            HangfireJobId: null);

        await taskRuns.InsertAsync(run, cancellationToken);
        await taskEvents.AppendAsync(
            new TaskEvent(
                Guid.NewGuid(),
                runId,
                "preprocess.daily_instance.created",
                "Succeeded",
                JsonSerializer.Serialize(new { scheduleId, windowStart, windowEnd }),
                null,
                null,
                now),
            cancellationToken);

        await schedules.UpdateAsync(
            schedule with
            {
                LastRunId = runId,
                LastRunStatus = TaskRunStatus.Queued.ToString(),
                LastRunEndAt = null,
                UpdatedAt = now
            },
            cancellationToken);

        scheduler.EnqueuePreprocess(runId);
        logger.LogInformation(
            "Daily preprocess instance {RunId} for schedule {ScheduleId} window {Start}..{End}",
            runId,
            scheduleId,
            windowStart,
            windowEnd);
    }

    public async Task UpdateScheduleFromRunAsync(TaskRun run, CancellationToken cancellationToken)
    {
        if (run.ScheduleId is not Guid scheduleId)
        {
            return;
        }

        var schedule = await schedules.GetByIdAsync(scheduleId, cancellationToken);
        if (schedule is null)
        {
            return;
        }

        await schedules.UpdateAsync(
            schedule with
            {
                LastRunId = run.RunId,
                LastRunStatus = run.Status.ToString(),
                LastRunEndAt = run.EndTime,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            cancellationToken);
    }

    public async Task DisableScheduleAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        var schedule = await schedules.GetByIdAsync(scheduleId, cancellationToken)
            ?? throw new TaskValidationException(TaskErrorCodes.NotFound, "定时计划不存在");

        if (!schedule.Enabled)
        {
            return;
        }

        scheduler.RemoveDailySchedule(schedule.HangfireRecurringId);
        var updated = schedule with { Enabled = false, UpdatedAt = DateTimeOffset.UtcNow };
        await schedules.UpdateAsync(updated, cancellationToken);
        logger.LogInformation("Disabled preprocess schedule {ScheduleId}", scheduleId);
    }

    public async Task DeleteScheduleAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        var schedule = await schedules.GetByIdAsync(scheduleId, cancellationToken).ConfigureAwait(false);
        if (schedule is null)
        {
            return;
        }

        scheduler.RemoveDailySchedule(schedule.HangfireRecurringId);
        await schedules.DeleteAsync(scheduleId, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Deleted preprocess schedule {ScheduleId}", scheduleId);
    }

    private static string BuildDailyInstanceKey(Guid scheduleId, DateOnly localDate)
    {
        var raw = $"DAILY|{scheduleId:N}|{localDate:yyyy-MM-dd}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}

public sealed record PreprocessScheduleCreateResult(Guid ScheduleId, string HangfireRecurringId);
