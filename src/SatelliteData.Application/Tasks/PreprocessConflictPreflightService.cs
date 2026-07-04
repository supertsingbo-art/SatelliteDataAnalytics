using SatelliteData.Application.Pipeline;
using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

public sealed record PreprocessConflictPreflightDto(
    bool HasConflict,
    string? ErrorCode,
    string? Message,
    IReadOnlyList<PreprocessConflictDetailDto>? ConflictDetails,
    string? PlanErrorCode,
    string? PlanErrorMessage);

public sealed class PreprocessConflictPreflightService(
    ITaskRunRepository taskRuns,
    IPreprocessParamClaimRepository paramClaims,
    PreprocessClaimPlanner claimPlanner,
    PreprocessConflictEnricher conflictEnricher)
{
    public async Task<PreprocessConflictPreflightDto> CheckAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await taskRuns.GetByRunIdAsync(runId, cancellationToken).ConfigureAwait(false)
            ?? throw new TaskValidationException(TaskErrorCodes.NotFound, "任务不存在");

        if (run.JobType != TaskJobType.Preprocess)
        {
            return new PreprocessConflictPreflightDto(
                false,
                null,
                null,
                null,
                "PRE_002",
                "仅预处理任务支持参数冲突预检");
        }

        var plan = await claimPlanner.PlanAsync(run, cancellationToken).ConfigureAwait(false);
        if (!plan.Succeeded)
        {
            return new PreprocessConflictPreflightDto(
                false,
                null,
                null,
                null,
                plan.ErrorCode,
                plan.ErrorMessage);
        }

        if (plan.Claims.Count == 0)
        {
            return new PreprocessConflictPreflightDto(false, null, null, null, null, null);
        }

        var probe = await paramClaims
            .ProbeConflictsAsync(runId, run.TasookNo, run.SatelliteNo, plan.Claims, cancellationToken)
            .ConfigureAwait(false);
        if (probe.Acquired)
        {
            return new PreprocessConflictPreflightDto(false, null, null, null, null, null);
        }

        var details = await conflictEnricher.EnrichAsync(run, probe.Conflicts, cancellationToken)
            .ConfigureAwait(false);
        var message = PreprocessConflictEnricher.BuildReadableMessage(details);
        return new PreprocessConflictPreflightDto(
            true,
            "PRE_006",
            message,
            details,
            null,
            null);
    }
}
