using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

public interface IPreprocessScheduleRepository
{
    Task<PreprocessSchedule?> GetByIdAsync(Guid scheduleId, CancellationToken cancellationToken);

    Task InsertAsync(PreprocessSchedule schedule, CancellationToken cancellationToken);

    Task UpdateAsync(PreprocessSchedule schedule, CancellationToken cancellationToken);
}
