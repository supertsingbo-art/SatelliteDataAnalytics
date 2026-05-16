namespace SatelliteData.Infrastructure;

/// <summary>6.1 资产缓存落库方式：默认 PostgreSQL（<see cref="PgAssetCacheRepository"/>）。</summary>
public sealed class AssetCacheOptions
{
    public const string SectionName = "AssetCache";

    /// <summary>为 true 时，海量/资产服务同步结果写入 <c>ConnectionStrings:Postgres</c> 对应库。</summary>
    public bool UsePostgreSql { get; init; } = true;
}
