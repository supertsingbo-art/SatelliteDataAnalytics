using System.Text.Json;
using SatelliteData.Domain.Templates;

namespace SatelliteData.Application.Templates;

public sealed class AlgorithmRegistryService(IAlgorithmPackageRepository packageRepository)
{
    /// <summary>
    /// 获取已发布的算法注册表，作为算法模板编辑器组件库面板的数据源。
    /// 同 <c>algorithmCode</c> 多版本时返回最新发布版本。
    /// </summary>
    public async Task<IReadOnlyCollection<AlgorithmRegistryEntry>> GetPublishedRegistryAsync(
        CancellationToken cancellationToken)
    {
        var packages = await packageRepository.GetAllAsync(cancellationToken);

        return packages
            .Where(p => p.Status == AlgorithmPackageStatus.Published)
            .GroupBy(p => p.AlgorithmCode, StringComparer.Ordinal)
            .Select(grp => grp.OrderByDescending(p => p.PublishedAt).First())
            .OrderBy(p => p.Category)
            .ThenBy(p => p.AlgorithmCode, StringComparer.Ordinal)
            .Select(p => new AlgorithmRegistryEntry(
                p.AlgorithmCode,
                p.DisplayName,
                p.Version,
                p.Runtime,
                p.Category,
                p.InputsSchemaJson,
                p.OutputsSchemaJson,
                p.ParamsSchemaJson,
                p.ResourcesJson,
                p.Description))
            .ToArray();
    }

    public async Task<bool> IsPublishedAsync(string algorithmCode, CancellationToken cancellationToken)
    {
        var packages = await packageRepository.GetAllAsync(cancellationToken);
        return packages.Any(p =>
            string.Equals(p.AlgorithmCode, algorithmCode, StringComparison.Ordinal)
            && p.Status == AlgorithmPackageStatus.Published);
    }

    public async Task<IReadOnlyCollection<AlgorithmPackageView>> ListAsync(
        AlgorithmPackageStatus? status,
        AlgorithmRuntime? runtime,
        AlgorithmCategory? category,
        CancellationToken cancellationToken)
    {
        var packages = await packageRepository.GetAllAsync(cancellationToken);
        IEnumerable<AlgorithmPackage> q = packages;
        if (status.HasValue) q = q.Where(p => p.Status == status.Value);
        if (runtime.HasValue) q = q.Where(p => p.Runtime == runtime.Value);
        if (category.HasValue) q = q.Where(p => p.Category == category.Value);

        return q.OrderBy(p => p.Category)
            .ThenBy(p => p.AlgorithmCode, StringComparer.Ordinal)
            .ThenByDescending(p => p.PublishedAt)
            .Select(ToView)
            .ToArray();
    }

    public async Task<AlgorithmPackageDetail?> GetDetailAsync(Guid packageId, CancellationToken cancellationToken)
    {
        var package = await packageRepository.GetByIdAsync(packageId, cancellationToken);
        return package is null ? null : new AlgorithmPackageDetail(
            ToView(package),
            package.InputsSchemaJson,
            package.OutputsSchemaJson,
            package.ParamsSchemaJson,
            package.ResourcesJson);
    }

    private static AlgorithmPackageView ToView(AlgorithmPackage package)
    {
        return new AlgorithmPackageView(
            package.PackageId,
            package.AlgorithmCode,
            package.DisplayName,
            package.Version,
            package.Runtime,
            package.Category,
            package.Status,
            package.Description,
            package.LastError,
            package.CreatedAt,
            package.UpdatedAt,
            package.PublishedAt);
    }
}
