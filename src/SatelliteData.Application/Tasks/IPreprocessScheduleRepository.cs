using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

public interface IPreprocessScheduleRepository
{
    Task<PreprocessSchedule?> GetByIdAsync(Guid scheduleId, CancellationToken cancellationToken);

    Task InsertAsync(PreprocessSchedule schedule, CancellationToken cancellationToken);

    Task UpdateAsync(PreprocessSchedule schedule, CancellationToken cancellationToken);

    Task<IReadOnlyList<PreprocessSchedule>> ListEnabledAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PreprocessSchedule>> ListByFilterTemplateIdAsync(Guid filterTemplateId, CancellationToken cancellationToken);

    Task DeleteAsync(Guid scheduleId, CancellationToken cancellationToken);
}
