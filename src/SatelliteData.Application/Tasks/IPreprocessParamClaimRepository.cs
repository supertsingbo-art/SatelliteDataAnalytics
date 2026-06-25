namespace SatelliteData.Application.Tasks;

public sealed record PreprocessParamClaimRequest(
    string ParamId,
    DateTimeOffset SegmentStart,
    DateTimeOffset SegmentEnd);

public sealed record PreprocessParamClaimConflict(
    string ParamId,
    Guid ConflictRunId,
    Guid ConflictFilterTemplateId,
    int ConflictFilterTemplateVersion);

public sealed record PreprocessParamClaimAcquireResult(
    bool Acquired,
    IReadOnlyList<string> ConflictParamIds,
    PreprocessParamClaimConflict? ConflictDetail)
{
    public static PreprocessParamClaimAcquireResult Success { get; } =
        new(true, Array.Empty<string>(), null);

    public static PreprocessParamClaimAcquireResult Conflict(
        IReadOnlyList<string> paramIds,
        PreprocessParamClaimConflict? detail) =>
        new(false, paramIds, detail);
}

public interface IPreprocessParamClaimRepository
{
    Task<PreprocessParamClaimAcquireResult> TryAcquireAsync(
        Guid runId,
        string tasookNo,
        string satelliteNo,
        Guid filterTemplateId,
        int filterTemplateVersion,
        IReadOnlyList<PreprocessParamClaimRequest> claims,
        CancellationToken cancellationToken);

    Task MarkCommittedByRunIdAsync(Guid runId, CancellationToken cancellationToken);

    Task ReleaseActiveByRunIdAsync(Guid runId, CancellationToken cancellationToken);

    Task DeleteByRunIdAsync(Guid runId, CancellationToken cancellationToken);
}
