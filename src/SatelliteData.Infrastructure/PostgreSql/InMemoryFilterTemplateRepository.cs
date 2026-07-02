using SatelliteData.Application.Templates;
using SatelliteData.Domain.Templates;

namespace SatelliteData.Infrastructure.PostgreSql;

public sealed class InMemoryFilterTemplateRepository : IFilterTemplateRepository
{
    private readonly Dictionary<(Guid TemplateId, int Version), FilterTemplate> _templates = [];
    private readonly object _gate = new();

    public Task<IReadOnlyCollection<FilterTemplate>> GetAllAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyCollection<FilterTemplate>>(_templates.Values.ToArray());
        }
    }

    public Task<IReadOnlyCollection<FilterTemplate>> GetByTemplateIdAsync(Guid templateId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var versions = _templates.Values
                .Where(item => item.TemplateId == templateId)
                .ToArray();
            return Task.FromResult<IReadOnlyCollection<FilterTemplate>>(versions);
        }
    }

    public Task<FilterTemplate?> GetVersionAsync(Guid templateId, int version, CancellationToken cancellationToken)
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

    public Task SaveAsync(FilterTemplate template, CancellationToken cancellationToken)
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

    public Task DeleteAllByTemplateIdAsync(Guid templateId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var keys = _templates.Keys
                .Where(k => k.TemplateId == templateId)
                .ToArray();
            foreach (var key in keys)
            {
                _templates.Remove(key);
            }

            return Task.CompletedTask;
        }
    }
}
