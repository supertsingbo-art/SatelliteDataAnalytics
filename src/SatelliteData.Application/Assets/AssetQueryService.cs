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

        if (request.EnabledOnly == true)
        {
            satellites = satellites.Where(item => item.IsEnabled).ToArray();
        }

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

    public async Task<SatelliteCache> SetSatelliteEnabledAsync(
        string tasookNo,
        string satelliteNo,
        bool isEnabled,
        CancellationToken cancellationToken)
    {
        var existing = await cacheRepository.GetSatelliteAsync(tasookNo, satelliteNo, cancellationToken);
        if (existing is null)
        {
            throw new InvalidOperationException("卫星缓存不存在");
        }

        await cacheRepository.SetSatelliteEnabledAsync(tasookNo, satelliteNo, isEnabled, cancellationToken);
        return (await cacheRepository.GetSatelliteAsync(tasookNo, satelliteNo, cancellationToken))!;
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
        const int maxPagedSize = 500;
        const int maxUnpagedSize = 50_000;

        var pageNo = request.PageNo <= 0 ? 1 : request.PageNo;
        var pageSize = request.Unpaged
            ? maxUnpagedSize
            : request.PageSize <= 0 ? 50 : Math.Min(request.PageSize, maxPagedSize);

        var parameters = FilterParameters(
            await cacheRepository.GetParametersAsync(tasookNo, satelliteNo, cancellationToken),
            request.Keyword);

        if (request.Unpaged)
        {
            var all = parameters.Select(ParamCacheViewMapper.ToView).ToArray();
            return new PagedResult<ParamCacheView>(1, all.Length, all.Length, all);
        }

        var items = parameters
            .Skip((pageNo - 1) * pageSize)
            .Take(pageSize)
            .Select(ParamCacheViewMapper.ToView)
            .ToArray();

        return new PagedResult<ParamCacheView>(pageNo, pageSize, parameters.Length, items);
    }

    private static ParamCache[] FilterParameters(IReadOnlyCollection<ParamCache> source, string? keyword)
    {
        var ordered = source.OrderBy(item => item.ParaId).ToArray();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return ordered;
        }

        var key = keyword.Trim();
        return ordered
            .Where(item => item.ParaId.ToString().Contains(key, StringComparison.OrdinalIgnoreCase)
                || (item.ParaCode?.Contains(key, StringComparison.OrdinalIgnoreCase) ?? false)
                || (item.ParaDesc?.Contains(key, StringComparison.OrdinalIgnoreCase) ?? false)
                || item.DisplayLabel.Contains(key, StringComparison.OrdinalIgnoreCase)
                || (item.ParaTypeDesc?.Contains(key, StringComparison.OrdinalIgnoreCase) ?? false)
                || (item.ProcDesc?.Contains(key, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToArray();
    }

    public async Task<PagedResult<CommandCacheView>> GetCommandsAsync(
        string tasookNo,
        string satelliteNo,
        AssetPageRequest request,
        CancellationToken cancellationToken)
    {
        var pageNo = request.PageNo <= 0 ? 1 : request.PageNo;
        var pageSize = request.PageSize <= 0 ? 50 : Math.Min(request.PageSize, 500);

        var commands = await cacheRepository.GetCommandsAsync(tasookNo, satelliteNo, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword!;
            commands = commands
                .Where(item => item.CmdId.ToString().Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || (item.CmdCode?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (item.CmdDesc?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToArray();
        }

        var items = commands
            .OrderBy(item => item.CmdId)
            .Skip((pageNo - 1) * pageSize)
            .Take(pageSize)
            .Select(CommandCacheViewMapper.ToView)
            .ToArray();

        return new PagedResult<CommandCacheView>(pageNo, pageSize, commands.Count, items);
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
