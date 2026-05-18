namespace SatelliteData.Infrastructure;

/// <summary>卫星分组持久化方式（<see cref="PostgreSql.PgSatelliteGroupRepository"/>）。</summary>
public sealed class SatelliteGroupOptions
{
    public const string SectionName = "SatelliteGroup";

    /// <summary>为 true 时，分组树与成员归属写入 <c>ConnectionStrings:Postgres</c>。</summary>
    public bool UsePostgreSql { get; init; } = true;
}
