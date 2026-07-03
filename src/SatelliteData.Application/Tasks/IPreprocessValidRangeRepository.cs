using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

public interface IPreprocessValidRangeRepository
{
    Task InsertBatchAsync(IReadOnlyList<PreprocessValidRange> ranges, CancellationToken cancellationToken);

    Task<IReadOnlyList<PreprocessValidRange>> ListByRunIdAsync(Guid runId, CancellationToken cancellationToken);

    Task DeleteByRunIdAsync(Guid runId, CancellationToken cancellationToken);
}
