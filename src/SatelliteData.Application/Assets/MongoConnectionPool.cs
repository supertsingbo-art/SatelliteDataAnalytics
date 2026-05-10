using SatelliteData.Domain.Assets;

namespace SatelliteData.Application.Assets;

/// <summary>
/// MongoDB 连接池摘要缓存。以 (tasookNo, satelliteNo) 为 key 缓存最近一次同步得到的 <see cref="MongoConnectionInfo"/>，
/// 当 Step 4 检测到 URI / source_version / authRef 变化时由 <see cref="AssetSyncService"/> 主动调用 <see cref="Invalidate"/> 失效。
/// </summary>
public sealed class MongoConnectionPool(IAssetCacheRepository cacheRepository)
{
    private readonly Dictionary<(string TasookNo, string SatelliteNo), MongoConnectionInfo> _cache = [];
    private readonly object _gate = new();

    public async Task<MongoConnectionInfo> GetConnectionInfoAsync(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue((tasookNo, satelliteNo), out var cached))
            {
                return cached;
            }
        }

        var satellite = await cacheRepository.GetSatelliteAsync(tasookNo, satelliteNo, cancellationToken)
            ?? throw new InvalidOperationException($"卫星缓存不存在：{tasookNo}/{satelliteNo}。");

        if (satellite.MongoInfo is null)
        {
            throw new InvalidOperationException($"该卫星的 Mongo 连接信息尚未同步：{tasookNo}/{satelliteNo}。");
        }

        lock (_gate)
        {
            _cache[(tasookNo, satelliteNo)] = satellite.MongoInfo;
        }

        return satellite.MongoInfo;
    }

    public void Invalidate(string tasookNo, string satelliteNo)
    {
        lock (_gate)
        {
            _cache.Remove((tasookNo, satelliteNo));
        }
    }

    public int CachedCount
    {
        get
        {
            lock (_gate)
            {
                return _cache.Count;
            }
        }
    }
}
