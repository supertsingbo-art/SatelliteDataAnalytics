using Microsoft.Extensions.Logging;
using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

public sealed class TaskRunLifecycleService(
    ITaskRunRepository taskRuns,
    ITaskEventRepository taskEvents,
    IHqParamMetadataRepository hqMetadata,
    IPreprocessOutlierSegmentRepository outlierSegments,
    IPreprocessOutlierPointReviewRepository outlierReviews,
    IBackgroundJobScheduler scheduler,
    ILogger<TaskRunLifecycleService> logger)
{
    public async Task DeleteRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await taskRuns.GetByRunIdAsync(runId, cancellationToken).ConfigureAwait(false)
            ?? throw new TaskValidationException(TaskErrorCodes.NotFound, "任务不存在");

        if (!TaskRunStateHelper.CanDeleteRun(run))
        {
            throw new TaskValidationException(TaskErrorCodes.NotDeletable, "仅已结束的预处理任务可删除");
        }

        await outlierReviews.DeleteByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        await outlierSegments.DeleteByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        await hqMetadata.DeleteByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        await taskEvents.DeleteByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        await taskRuns.DeleteAsync(runId, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Deleted preprocess task run {RunId}", runId);
    }

    public async Task<ExecuteTaskResultDto> ReExecuteRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await taskRuns.GetByRunIdAsync(runId, cancellationToken).ConfigureAwait(false)
            ?? throw new TaskValidationException(TaskErrorCodes.NotFound, "任务不存在");

        if (!TaskRunStateHelper.CanReExecuteRun(run))
        {
            throw new TaskValidationException(TaskErrorCodes.NotReExecutable, "仅已结束的立即预处理任务可重复执行");
        }

        run = run with
        {
            Status = TaskRunStatus.Queued,
            HangfireJobId = null,
            CurrentStep = "pending",
            ProgressPercent = 3m,
            StartTime = null,
            EndTime = null,
            ErrorCode = null,
            ErrorMsg = null,
            TimeoutFlag = false,
            ExecutionMode = run.ExecutionMode ?? PreprocessExecutionMode.Immediate
        };
        await taskRuns.UpdateAsync(run, cancellationToken).ConfigureAwait(false);

        var hangfireId = scheduler.EnqueuePreprocess(runId);
        run = run with { HangfireJobId = hangfireId, CurrentStep = "queued" };
        await taskRuns.UpdateAsync(run, cancellationToken).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        logger.LogInformation("Re-execute preprocess {RunId}, Hangfire {JobId}", runId, hangfireId);
        return new ExecuteTaskResultDto(
            TaskDisplayStatus.ForRun(run, now),
            run.RunId,
            run.ScheduleId,
            run.JobId,
            run.Status.ToString());
    }
}
