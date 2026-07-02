using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

public sealed class TaskOrchestrator(
    ITaskRunRepository taskRuns,
    ITaskEventRepository taskEvents,
    IBackgroundJobScheduler scheduler,
    ITaskRunCancellationRegistry cancellationRegistry,
    PreprocessTaskValidator preprocessValidator,
    PreprocessScheduleService scheduleService,
    ILogger<TaskOrchestrator> logger)
{
    public async Task<PipelineCreateResult> CreatePipelineAsync(
        PipelineCreateCommand command,
        Guid? createdBy,
        CancellationToken cancellationToken)
    {
        EnsureValidWindow(command.WindowStart, command.WindowEnd);

        var idempotencyKey = string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? BuildIdempotencyKey(command)
            : command.IdempotencyKey.Trim();

        var existing = await taskRuns.GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return new PipelineCreateResult(existing.RunId, existing.JobId, existing.Status, Created: false);
        }

        var runId = Guid.NewGuid();
        var jobId = $"JOB-{DateTime.UtcNow:yyyyMMddHHmmss}-{runId.ToString("N")[..8]}";
        var now = DateTimeOffset.UtcNow;

        var run = new TaskRun(
            RunId: runId,
            ParentRunId: null,
            JobId: jobId,
            JobType: TaskJobType.Pipeline,
            TriggerType: command.TriggerType,
            Status: TaskRunStatus.Queued,
            IdempotencyKey: idempotencyKey,
            TasookNo: command.TasookNo,
            SatelliteNo: command.SatelliteNo,
            TestBatchName: command.TestBatchName,
            WindowStart: command.WindowStart,
            WindowEnd: command.WindowEnd,
            FilterTemplateId: command.FilterTemplateId,
            FilterTemplateVersion: command.FilterTemplateVersion,
            AlgorithmTemplateId: command.AlgorithmTemplateId,
            AlgorithmTemplateVersion: command.AlgorithmTemplateVersion,
            ReportTemplateId: null,
            ReportTemplateVersion: null,
            ProgressPercent: 3m,
            CurrentStep: "Queued",
            StartTime: null,
            EndTime: null,
            TimeoutFlag: false,
            ErrorCode: null,
            ErrorMsg: null,
            CreatedBy: createdBy,
            CreatedAt: now);

        await taskRuns.InsertAsync(run, cancellationToken);
        await taskEvents.AppendAsync(
            new TaskEvent(
                Guid.NewGuid(),
                runId,
                "pipeline.created",
                "Succeeded",
                JsonSerializer.Serialize(new { jobId, command.TasookNo, command.SatelliteNo }),
                null,
                null,
                now),
            cancellationToken);

        var hangfireId = scheduler.EnqueuePreprocess(runId);
        logger.LogInformation("Pipeline {RunId} queued, Hangfire {HangfireId}", runId, hangfireId);

        return new PipelineCreateResult(runId, jobId, TaskRunStatus.Queued, Created: true);
    }

    public async Task<PreprocessCreateResult> CreatePreprocessAsync(
        PreprocessCreateCommand command,
        Guid? createdBy,
        CancellationToken cancellationToken)
    {
        if (command.CreateMode == PreprocessCreateMode.DailyRecurring)
        {
            var sched = await scheduleService.CreateDailyScheduleAsync(command, createdBy, cancellationToken);
            return new PreprocessCreateResult(
                RunId: null,
                ScheduleId: sched.ScheduleId,
                JobId: null,
                Status: TaskRunStatus.Queued,
                Created: true);
        }

        await preprocessValidator.ValidateAsync(command, cancellationToken);

        var idempotencyKey = string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? BuildPreprocessIdempotencyKey(command)
            : command.IdempotencyKey.Trim();

        var existing = await taskRuns.GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return new PreprocessCreateResult(
                existing.RunId,
                existing.ScheduleId,
                existing.JobId,
                existing.Status,
                Created: false);
        }

        var runId = Guid.NewGuid();
        var jobId = $"PRE-{DateTime.UtcNow:yyyyMMddHHmmss}-{runId.ToString("N")[..8]}";
        var now = DateTimeOffset.UtcNow;
        var isOnceScheduled = command.CreateMode == PreprocessCreateMode.OnceScheduled;
        var executionMode = isOnceScheduled
            ? PreprocessExecutionMode.OnceScheduled
            : PreprocessExecutionMode.Immediate;
        var triggerType = isOnceScheduled ? TaskTriggerType.Scheduled : command.TriggerType;

        var run = new TaskRun(
            RunId: runId,
            ParentRunId: null,
            JobId: jobId,
            JobType: TaskJobType.Preprocess,
            TriggerType: triggerType,
            Status: TaskRunStatus.Queued,
            IdempotencyKey: idempotencyKey,
            TasookNo: command.TasookNo,
            SatelliteNo: command.SatelliteNo,
            TestBatchName: command.TestBatchName,
            WindowStart: command.WindowStart,
            WindowEnd: command.WindowEnd,
            FilterTemplateId: command.FilterTemplateId,
            FilterTemplateVersion: command.FilterTemplateVersion,
            AlgorithmTemplateId: null,
            AlgorithmTemplateVersion: null,
            ReportTemplateId: null,
            ReportTemplateVersion: null,
            ProgressPercent: 3m,
            CurrentStep: isOnceScheduled ? "scheduled" : "pending",
            StartTime: null,
            EndTime: null,
            TimeoutFlag: false,
            ErrorCode: null,
            ErrorMsg: null,
            CreatedBy: createdBy,
            CreatedAt: now,
            ExecutionMode: executionMode,
            ScheduledAt: isOnceScheduled ? command.ScheduledAt : null,
            ScheduleId: null,
            HangfireJobId: null);

        await taskRuns.InsertAsync(run, cancellationToken);
        await taskEvents.AppendAsync(
            new TaskEvent(
                Guid.NewGuid(),
                runId,
                "preprocess.created",
                "Succeeded",
                JsonSerializer.Serialize(new
                {
                    jobId,
                    command.TasookNo,
                    command.SatelliteNo,
                    mode = PreprocessCreateModeParser.ToApi(command.CreateMode)
                }),
                null,
                null,
                now),
            cancellationToken);

        string hangfireId;
        if (isOnceScheduled)
        {
            hangfireId = scheduler.SchedulePreprocess(runId, command.ScheduledAt!.Value);
            run = run with { HangfireJobId = hangfireId };
            await taskRuns.UpdateAsync(run, cancellationToken);
            logger.LogInformation(
                "Preprocess {RunId} scheduled at {At}, Hangfire {HangfireId}",
                runId,
                command.ScheduledAt,
                hangfireId);
        }
        else
        {
            logger.LogInformation("Preprocess {RunId} created pending manual execute", runId);
        }

        return new PreprocessCreateResult(runId, null, jobId, TaskRunStatus.Queued, Created: true);
    }

    public async Task<PipelineCreateResult> CancelAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await taskRuns.GetByRunIdAsync(runId, cancellationToken);
        if (run is null)
        {
            throw new TaskValidationException(TaskErrorCodes.NotFound, "任务不存在");
        }

        if (run.Status is TaskRunStatus.Succeeded or TaskRunStatus.Failed or TaskRunStatus.Timeout
            or TaskRunStatus.Cancelled)
        {
            throw new TaskValidationException(
                TaskErrorCodes.NotCancellable,
                $"任务已结束，无法取消（当前状态：{run.Status}）");
        }

        if (run.ExecutionMode == PreprocessExecutionMode.OnceScheduled
            && !string.IsNullOrWhiteSpace(run.HangfireJobId))
        {
            scheduler.TryDeleteScheduledJob(run.HangfireJobId);
        }

        var now = DateTimeOffset.UtcNow;
        run = run with
        {
            Status = TaskRunStatus.Cancelled,
            CurrentStep = "cancelled",
            EndTime = now
        };
        await taskRuns.UpdateAsync(run, cancellationToken);
        cancellationRegistry.Cancel(runId);
        await taskEvents.AppendAsync(
            new TaskEvent(
                Guid.NewGuid(),
                runId,
                "task.cancelled",
                "Cancelled",
                JsonSerializer.Serialize(new { run.JobId }),
                null,
                null,
                now),
            cancellationToken);

        logger.LogInformation("Task {RunId} cancelled by user", runId);
        return new PipelineCreateResult(run.RunId, run.JobId, TaskRunStatus.Cancelled, Created: false);
    }

    private static void EnsureValidWindow(DateTimeOffset? windowStart, DateTimeOffset? windowEnd)
    {
        if (windowStart is null || windowEnd is null) return;
        if (windowStart.Value >= windowEnd.Value) throw new InvalidTaskWindowException();
    }

    private static string BuildIdempotencyKey(PipelineCreateCommand command)
    {
        var raw =
            $"{command.TasookNo}|{command.SatelliteNo}|{command.TestBatchName}|{command.WindowStart:o}|{command.WindowEnd:o}|{command.FilterTemplateId}|{command.FilterTemplateVersion}|{command.AlgorithmTemplateId}|{command.AlgorithmTemplateVersion}|{command.TriggerType}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    private static string BuildPreprocessIdempotencyKey(PreprocessCreateCommand command)
    {
        var raw =
            $"PREPROCESS|{PreprocessCreateModeParser.ToApi(command.CreateMode)}|{command.TasookNo}|{command.SatelliteNo}|{command.TestBatchName}|{command.WindowStart:o}|{command.WindowEnd:o}|{command.ScheduledAt:o}|{command.FilterTemplateId}|{command.FilterTemplateVersion}|{command.TriggerType}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}

public sealed record PreprocessCreateCommand(
    string TasookNo,
    string SatelliteNo,
    string? TestBatchName,
    DateTimeOffset? WindowStart,
    DateTimeOffset? WindowEnd,
    Guid FilterTemplateId,
    int FilterTemplateVersion,
    string? IdempotencyKey,
    TaskTriggerType TriggerType,
    PreprocessCreateMode CreateMode = PreprocessCreateMode.Immediate,
    DateTimeOffset? ScheduledAt = null,
    TimeOnly? DailyTime = null,
    int? IntervalDays = null,
    DateOnly? EffectiveFrom = null);

public sealed record PipelineCreateCommand(
    string TasookNo,
    string SatelliteNo,
    string? TestBatchName,
    DateTimeOffset? WindowStart,
    DateTimeOffset? WindowEnd,
    Guid FilterTemplateId,
    int FilterTemplateVersion,
    Guid AlgorithmTemplateId,
    int AlgorithmTemplateVersion,
    string? IdempotencyKey,
    TaskTriggerType TriggerType);

public sealed record PipelineCreateResult(Guid RunId, string JobId, TaskRunStatus Status, bool Created);

public sealed record PreprocessCreateResult(
    Guid? RunId,
    Guid? ScheduleId,
    string? JobId,
    TaskRunStatus Status,
    bool Created);
