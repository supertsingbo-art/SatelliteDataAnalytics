namespace SatelliteData.Infrastructure;

/// <summary>
/// 模板治理与数据源配置等非资产缓存类仓储的 PostgreSQL 开关。
/// </summary>
public sealed class InfrastructureStorageOptions
{
    public const string SectionName = "InfrastructureStorage";

    /// <summary>为 true 时使用 <c>ConnectionStrings:Postgres</c> 持久化筛选/算法模板、算法包与数据源配置。</summary>
    public bool UsePostgreSql { get; init; } = true;
}
