using SatelliteData.Domain.Assets;

namespace SatelliteData.Application.Assets;

/// <summary>
/// 资产查询应用服务。封装资产中心的只读查询用例：卫星列表 / 单星 / 参数 / 测试阶段 / Mongo 摘要。
/// </summary>
public sealed class AssetQueryService(
    IAssetCacheRepository cacheRepository,
    MongoConnectionPool mongoConnectionPool)
{
    public async Task<PagedResult<SatelliteListItem>> GetSatellitesAsync(
        AssetPageRequest request,
        CancellationToken cancellationToken)
    {
        var pageNo = request.PageNo <= 0 ? 1 : request.PageNo;
        var pageSize = request.PageSize <= 0 ? 50 : Math.Min(request.PageSize, 500);

        var satellites = await cacheRepository.GetSatellitesAsync(cancellationToken);
        var phaseLabels = await cacheRepository.GetDevelopmentPhaseLabelsBySatelliteAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword!;
            satellites = satellites
                .Where(item => item.SatelliteName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || item.SatelliteNo.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || item.TasookNo.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || (item.TasookName?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToArray();
        }

        var ordered = satellites
            .OrderBy(item => item.TasookNo, StringComparer.Ordinal)
            .ThenBy(item => item.SatelliteNo, StringComparer.Ordinal)
            .ToArray();

        var pageItems = ordered
            .Skip((pageNo - 1) * pageSize)
            .Take(pageSize)
            .Select(s =>
            {
                phaseLabels.TryGetValue((s.TasookNo, s.SatelliteNo), out var phases);
                return SatelliteListItemMapper.ToListItem(s, phases ?? Array.Empty<string>());
            })
            .ToArray();

        return new PagedResult<SatelliteListItem>(pageNo, pageSize, ordered.Length, pageItems);
    }

    public Task<SatelliteCache?> GetSatelliteAsync(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken)
    {
        return cacheRepository.GetSatelliteAsync(tasookNo, satelliteNo, cancellationToken);
    }

    public async Task<PagedResult<ParamCacheView>> GetParametersAsync(
        string tasookNo,
        string satelliteNo,
        AssetPageRequest request,
        CancellationToken cancellationToken)
    {
        var pageNo = request.PageNo <= 0 ? 1 : request.PageNo;
        var pageSize = request.PageSize <= 0 ? 50 : Math.Min(request.PageSize, 500);

        var parameters = await cacheRepository.GetParametersAsync(tasookNo, satelliteNo, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword!;
            parameters = parameters
                .Where(item => item.ParamId.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || item.ParaId.ToString().Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || (item.ParaCode?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (item.ParaDesc?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (item.ParaTypeDesc?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (item.ProcDesc?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToArray();
        }

        var items = parameters
            .OrderBy(item => item.ParaId)
            .Skip((pageNo - 1) * pageSize)
            .Take(pageSize)
            .Select(ParamCacheViewMapper.ToView)
            .ToArray();

        return new PagedResult<ParamCacheView>(pageNo, pageSize, parameters.Count, items);
    }

    public Task<IReadOnlyCollection<TestBatchCache>> GetTestPhasesAsync(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken)
    {
        return cacheRepository.GetTestBatchesAsync(tasookNo, satelliteNo, cancellationToken);
    }

    public async Task<MongoConnectionSummary> GetMongoInfoSummaryAsync(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken)
    {
        var mongoInfo = await mongoConnectionPool.GetConnectionInfoAsync(tasookNo, satelliteNo, cancellationToken);
        return new MongoConnectionSummary(
            mongoInfo.DbName,
            mongoInfo.AuthRef,
            MongoUriSanitizer.MaskCredentials(mongoInfo.MongoUri));
    }
}

public sealed record MongoConnectionSummary(
    string DbName,
    string? AuthRef,
    string MongoUri);
