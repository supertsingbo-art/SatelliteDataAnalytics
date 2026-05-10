using SatelliteData.Domain.Assets;

namespace SatelliteData.Application.Assets;

/// <summary>
/// 资产查询应用服务。封装资产中心的只读查询用例：卫星列表 / 单星 / 参数 / 测试阶段 / Mongo 摘要。
/// </summary>
public sealed class AssetQueryService(
    IAssetCacheRepository cacheRepository,
    MongoConnectionPool mongoConnectionPool)
{
    public async Task<PagedResult<SatelliteCache>> GetSatellitesAsync(
        AssetPageRequest request,
        CancellationToken cancellationToken)
    {
        var pageNo = request.PageNo <= 0 ? 1 : request.PageNo;
        var pageSize = request.PageSize <= 0 ? 50 : Math.Min(request.PageSize, 500);

        var satellites = await cacheRepository.GetSatellitesAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword!;
            satellites = satellites
                .Where(item => item.SatelliteName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || item.SatelliteNo.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || item.TasookNo.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        var ordered = satellites
            .OrderBy(item => item.TasookNo, StringComparer.Ordinal)
            .ThenBy(item => item.SatelliteNo, StringComparer.Ordinal)
            .ToArray();

        var items = ordered
            .Skip((pageNo - 1) * pageSize)
            .Take(pageSize)
            .ToArray();

        return new PagedResult<SatelliteCache>(pageNo, pageSize, ordered.Length, items);
    }

    public Task<SatelliteCache?> GetSatelliteAsync(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken)
    {
        return cacheRepository.GetSatelliteAsync(tasookNo, satelliteNo, cancellationToken);
    }

    public async Task<PagedResult<ParamCache>> GetParametersAsync(
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
            parameters = parameters
                .Where(item => item.ParamId.Contains(request.Keyword!, StringComparison.OrdinalIgnoreCase)
                    || item.ParamName.Contains(request.Keyword!, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        var items = parameters
            .Skip((pageNo - 1) * pageSize)
            .Take(pageSize)
            .ToArray();

        return new PagedResult<ParamCache>(pageNo, pageSize, parameters.Count, items);
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
