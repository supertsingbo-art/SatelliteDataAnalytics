using SatelliteData.Domain.Assets;

namespace SatelliteData.Application.Assets;

/// <summary>
/// 资产查询应用服务。负责封装资产中心的只读查询用例，
/// 屏蔽 Controller 与 <see cref="IAssetCacheRepository"/> 之间的直接依赖，
/// 便于后续叠加权限过滤、字段脱敏、统一日志等业务逻辑。
/// </summary>
public sealed class AssetQueryService(
    IAssetCacheRepository cacheRepository,
    MongoConnectionPool mongoConnectionPool)
{
    public Task<IReadOnlyCollection<SatelliteCache>> GetSatellitesAsync(CancellationToken cancellationToken)
    {
        return cacheRepository.GetSatellitesAsync(cancellationToken);
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

    public Task<IReadOnlyCollection<TestBatchCache>> GetTestBatchesAsync(
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
            MaskMongoUri(mongoInfo.MongoUri));
    }

    private static string MaskMongoUri(string uri)
    {
        var at = uri.IndexOf('@', StringComparison.Ordinal);
        var scheme = uri.IndexOf("://", StringComparison.Ordinal);
        if (at > 0 && scheme > 0 && at > scheme)
        {
            return uri[..(scheme + 3)] + "***:***" + uri[at..];
        }

        return uri;
    }
}

public sealed record MongoConnectionSummary(
    string DbName,
    string? AuthRef,
    string MongoUri);
