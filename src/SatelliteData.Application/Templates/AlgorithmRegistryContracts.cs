using System.Text.Json;
using SatelliteData.Domain.Templates;

namespace SatelliteData.Application.Templates;

public sealed record AlgorithmRegistryEntry(
    string AlgorithmCode,
    string DisplayName,
    string Version,
    AlgorithmRuntime Runtime,
    AlgorithmCategory Category,
    JsonElement InputsSchemaJson,
    JsonElement OutputsSchemaJson,
    JsonElement ParamsSchemaJson,
    JsonElement ResourcesJson,
    string? Description);

public sealed record AlgorithmPackageView(
    Guid PackageId,
    string AlgorithmCode,
    string DisplayName,
    string Version,
    AlgorithmRuntime Runtime,
    AlgorithmCategory Category,
    AlgorithmPackageStatus Status,
    string? Description,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt);

public sealed record AlgorithmPackageDetail(
    AlgorithmPackageView View,
    JsonElement InputsSchemaJson,
    JsonElement OutputsSchemaJson,
    JsonElement ParamsSchemaJson,
    JsonElement ResourcesJson);

public interface IAlgorithmPackageRepository
{
    Task<IReadOnlyCollection<AlgorithmPackage>> GetAllAsync(CancellationToken cancellationToken);

    Task<AlgorithmPackage?> GetByIdAsync(Guid packageId, CancellationToken cancellationToken);

    Task<AlgorithmPackage?> GetByCodeAndVersionAsync(string algorithmCode, string version, CancellationToken cancellationToken);

    Task SaveAsync(AlgorithmPackage package, CancellationToken cancellationToken);

    Task DeleteAsync(Guid packageId, CancellationToken cancellationToken);
}
