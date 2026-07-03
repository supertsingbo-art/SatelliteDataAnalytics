namespace SatelliteData.Application.Tasks;

public sealed record PreprocessParamClaimRequest(
    string ParamId,
    DateTimeOffset SegmentStart,
    DateTimeOffset SegmentEnd);

public sealed record PreprocessParamClaimConflict(
    string ParamId,
    Guid ConflictRunId,
    Guid ConflictFilterTemplateId,
    int ConflictFilterTemplateVersion,
    string ConflictStatus);

public sealed record PreprocessParamClaimAcquireResult(
    bool Acquired,
    IReadOnlyList<PreprocessParamClaimConflict> Conflicts)
{
    public IReadOnlyList<string> ConflictParamIds => Conflicts
        .Select(x => x.ParamId)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(x => x, StringComparer.Ordinal)
        .ToArray();

    public PreprocessParamClaimConflict? ConflictDetail => Conflicts.FirstOrDefault();

    public static PreprocessParamClaimAcquireResult Success { get; } =
        new(true, Array.Empty<PreprocessParamClaimConflict>());

    public static PreprocessParamClaimAcquireResult Conflict(
        IReadOnlyList<PreprocessParamClaimConflict> details) =>
        new(false, details);
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

    Task DeleteCommittedOverlapsAsync(
        Guid runId,
        string tasookNo,
        string satelliteNo,
        IReadOnlyList<PreprocessParamClaimRequest> claims,
        CancellationToken cancellationToken);

    Task ReleaseActiveByRunIdAsync(Guid runId, CancellationToken cancellationToken);

    Task DeleteByRunIdAsync(Guid runId, CancellationToken cancellationToken);
}
