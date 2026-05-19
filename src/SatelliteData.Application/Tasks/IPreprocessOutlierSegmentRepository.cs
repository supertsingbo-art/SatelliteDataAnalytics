using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

public interface IPreprocessOutlierSegmentRepository
{
    Task InsertBatchAsync(IReadOnlyList<PreprocessOutlierSegment> segments, CancellationToken cancellationToken);

    Task<IReadOnlyList<PreprocessOutlierSegment>> ListByRunIdAsync(Guid runId, CancellationToken cancellationToken);
}
