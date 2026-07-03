using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

public interface IOutlierMarkConfigRepository
{
    Task<IReadOnlyList<OutlierMarkOption>> ListAsync(CancellationToken cancellationToken);

    Task ReplaceAllAsync(IReadOnlyList<OutlierMarkOption> options, CancellationToken cancellationToken);
}
