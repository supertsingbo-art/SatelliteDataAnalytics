using Microsoft.Extensions.Logging;
using SatelliteData.Application.Pipeline;
using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

public sealed class TaskRunLifecycleService(
    ITaskRunRepository taskRuns,
    ITaskEventRepository taskEvents,
    IHqParamMetadataRepository hqMetadata,
    IPreprocessParamClaimRepository paramClaims,
    IPreprocessOutlierSegmentRepository outlierSegments,
    IPreprocessOutlierPointReviewRepository outlierReviews,
    IPreprocessValidRangeRepository validRanges,
    ITaskRunConflictOptionStore conflictOptionStore,
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

        await DeleteRunCoreAsync(runId, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Deleted preprocess task run {RunId}", runId);
    }

    public async Task DeleteRunForceAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await taskRuns.GetByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            return;
        }

        await DeleteRunCoreAsync(runId, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Force deleted task run {RunId}", runId);
    }

    public async Task<ExecuteTaskResultDto> ReExecuteRunAsync(
        Guid runId,
        PreprocessConflictHandlingOptions? conflictOptions,
        CancellationToken cancellationToken)
    {
        var run = await taskRuns.GetByRunIdAsync(runId, cancellationToken).ConfigureAwait(false)
            ?? throw new TaskValidationException(TaskErrorCodes.NotFound, "任务不存在");

        if (!TaskRunStateHelper.CanReExecuteRun(run))
        {
            throw new TaskValidationException(
                TaskErrorCodes.NotReExecutable,
                "仅已结束的立即预处理任务或启用筛选模板的 PIPELINE 任务可重复执行");
        }

        var usePreprocess = run.JobType == TaskJobType.Preprocess
                            || run.FilterTemplateId is not null;
        run = run with
        {
            Status = TaskRunStatus.Queued,
            HangfireJobId = null,
            CurrentStep = usePreprocess ? "preprocess_queued" : "algorithm_queued",
            ProgressPercent = usePreprocess ? 3m : TaskProgressBands.PreprocessMax,
            StartTime = null,
            EndTime = null,
            ErrorCode = null,
            ErrorMsg = null,
            TimeoutFlag = false,
            ExecutionMode = run.ExecutionMode ?? PreprocessExecutionMode.Immediate
        };
        await taskRuns.UpdateAsync(run, cancellationToken).ConfigureAwait(false);

        var hangfireId = usePreprocess
            ? scheduler.EnqueuePreprocess(runId)
            : scheduler.EnqueueAlgorithm(runId);
        if (conflictOptions is null)
        {
            conflictOptionStore.Clear(runId);
        }
        else
        {
            conflictOptionStore.Set(runId, conflictOptions);
        }
        run = run with { HangfireJobId = hangfireId, CurrentStep = usePreprocess ? "preprocess_queued" : "algorithm_queued" };
        await taskRuns.UpdateAsync(run, cancellationToken).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        logger.LogInformation(
            "Re-execute task {RunId} ({Mode}), Hangfire {JobId}",
            runId,
            usePreprocess ? "preprocess" : "algorithm",
            hangfireId);
        return new ExecuteTaskResultDto(
            TaskDisplayStatus.ForRun(run, now),
            run.RunId,
            run.ScheduleId,
            run.JobId,
            run.Status.ToString());
    }

    private async Task DeleteRunCoreAsync(Guid runId, CancellationToken cancellationToken)
    {
        await outlierReviews.DeleteByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        await outlierSegments.DeleteByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        await validRanges.DeleteByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        await hqMetadata.DeleteByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        await paramClaims.DeleteByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        await taskEvents.DeleteByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        await taskRuns.DeleteAsync(runId, cancellationToken).ConfigureAwait(false);
    }
}
