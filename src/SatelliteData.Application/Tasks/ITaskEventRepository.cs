using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

public interface ITaskEventRepository
{
    Task AppendAsync(TaskEvent evt, CancellationToken cancellationToken);
}
