using MongoDB.Bson;

namespace SatelliteData.Infrastructure.Mongo;

/// <summary>MongoDB BSON 日期映射：UTC → 服务器本地时间。</summary>
public static class MongoDateTimeMapper
{
    public static DateTimeOffset ToLocalOffset(BsonValue value)
    {
        if (!value.IsValidDateTime)
        {
            return default;
        }

        var utc = DateTime.SpecifyKind(value.ToUniversalTime(), DateTimeKind.Utc);
        var local = utc.ToLocalTime();
        return new DateTimeOffset(local);
    }
}
