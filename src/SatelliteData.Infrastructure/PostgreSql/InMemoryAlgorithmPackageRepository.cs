using SatelliteData.Application.Templates;
using SatelliteData.Domain.Templates;

namespace SatelliteData.Infrastructure.PostgreSql;

public sealed class InMemoryAlgorithmPackageRepository : IAlgorithmPackageRepository
{
    private readonly Dictionary<Guid, AlgorithmPackage> _packages = [];
    private readonly object _gate = new();

    public InMemoryAlgorithmPackageRepository()
    {
        foreach (var package in AlgorithmPackageBuiltinSeed.CreatePackages())
        {
            _packages[package.PackageId] = package;
        }
    }

    public Task<IReadOnlyCollection<AlgorithmPackage>> GetAllAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyCollection<AlgorithmPackage>>(_packages.Values.ToArray());
        }
    }

    public Task<AlgorithmPackage?> GetByIdAsync(Guid packageId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _packages.TryGetValue(packageId, out var p);
            return Task.FromResult(p);
        }
    }

    public Task<AlgorithmPackage?> GetByCodeAndVersionAsync(string algorithmCode, string version, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var p = _packages.Values.FirstOrDefault(item =>
                string.Equals(item.AlgorithmCode, algorithmCode, StringComparison.Ordinal)
                && string.Equals(item.Version, version, StringComparison.Ordinal));
            return Task.FromResult(p);
        }
    }

    public Task SaveAsync(AlgorithmPackage package, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _packages[package.PackageId] = package;
            return Task.CompletedTask;
        }
    }

    public Task DeleteAsync(Guid packageId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _packages.Remove(packageId);
            return Task.CompletedTask;
        }
    }

}
