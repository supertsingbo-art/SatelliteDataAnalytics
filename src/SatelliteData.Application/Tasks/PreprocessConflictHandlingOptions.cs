namespace SatelliteData.Application.Tasks;

public enum ActiveConflictHandling
{
    Fail = 0,
    Skip = 1
}

public enum CommittedConflictHandling
{
    Fail = 0,
    Skip = 1,
    Overwrite = 2
}

public sealed record PreprocessConflictHandlingOptions(
    ActiveConflictHandling OnActiveConflict = ActiveConflictHandling.Fail,
    CommittedConflictHandling OnCommittedConflict = CommittedConflictHandling.Fail);

public interface ITaskRunConflictOptionStore
{
    void Set(Guid runId, PreprocessConflictHandlingOptions options);

    bool TryGet(Guid runId, out PreprocessConflictHandlingOptions options);

    void Clear(Guid runId);
}
