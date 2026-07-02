namespace SatelliteData.Infrastructure.PostgreSql;

/// <summary>Npgsql 6+ 写入 <c>timestamptz</c> 要求 <see cref="DateTimeOffset"/> 偏移为 UTC。</summary>
public static class PgTimestamptz
{
    public static DateTimeOffset Utc(DateTimeOffset value) => value.ToUniversalTime();

    public static object UtcOrDbNull(DateTimeOffset? value) =>
        value is { } v ? v.ToUniversalTime() : DBNull.Value;
}
