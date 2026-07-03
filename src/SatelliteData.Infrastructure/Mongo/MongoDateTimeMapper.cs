using MongoDB.Bson;

namespace SatelliteData.Infrastructure.Mongo;

/// <summary>MongoDB BSON 日期映射：统一返回 UTC 偏移。</summary>
public static class MongoDateTimeMapper
{
    public static DateTimeOffset ToUtcOffset(BsonValue value)
    {
        if (!value.IsValidDateTime)
        {
            return default;
        }

        var utc = DateTime.SpecifyKind(value.ToUniversalTime(), DateTimeKind.Utc);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }
}
