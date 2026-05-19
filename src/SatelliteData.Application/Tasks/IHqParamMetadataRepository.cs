namespace SatelliteData.Application.Tasks;

public sealed record HqParamMetadataRow(
    Guid MetadataId,
    Guid RunId,
    string TasookNo,
    string SatelliteNo,
    string TestBatchId,
    string ParamId,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    Guid FilterTemplateId,
    int FilterTemplateVersion,
    string? OutlierMethod,
    string? OutlierReasonPattern);

public interface IHqParamMetadataRepository
{
    Task InsertAsync(HqParamMetadataRow row, CancellationToken cancellationToken);

    Task<IReadOnlyList<HqParamMetadataRow>> ListByRunIdAsync(Guid runId, CancellationToken cancellationToken);

    Task DeleteByRunIdAsync(Guid runId, CancellationToken cancellationToken);
}
