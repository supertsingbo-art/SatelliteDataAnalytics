using SatelliteData.Application.Templates;
using SatelliteData.Domain.Templates;

namespace SatelliteData.Infrastructure.PostgreSql;

public sealed class InMemoryAlgorithmTemplateRepository : IAlgorithmTemplateRepository
{
    private readonly Dictionary<(Guid TemplateId, int Version), AlgorithmTemplate> _templates = [];
    private readonly object _gate = new();

    public Task<IReadOnlyCollection<AlgorithmTemplate>> GetAllAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyCollection<AlgorithmTemplate>>(_templates.Values.ToArray());
        }
    }

    public Task<IReadOnlyCollection<AlgorithmTemplate>> GetByTemplateIdAsync(Guid templateId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var versions = _templates.Values
                .Where(item => item.TemplateId == templateId)
                .ToArray();
            return Task.FromResult<IReadOnlyCollection<AlgorithmTemplate>>(versions);
        }
    }

    public Task<AlgorithmTemplate?> GetVersionAsync(Guid templateId, int version, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _templates.TryGetValue((templateId, version), out var template);
            return Task.FromResult(template);
        }
    }

    public Task<int> GetMaxVersionAsync(Guid templateId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var max = _templates.Values
                .Where(item => item.TemplateId == templateId)
                .Select(item => item.Version)
                .DefaultIfEmpty(0)
                .Max();
            return Task.FromResult(max);
        }
    }

    public Task SaveAsync(AlgorithmTemplate template, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _templates[(template.TemplateId, template.Version)] = template;
            return Task.CompletedTask;
        }
    }

    public Task DeleteAsync(Guid templateId, int version, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _templates.Remove((templateId, version));
            return Task.CompletedTask;
        }
    }
}
