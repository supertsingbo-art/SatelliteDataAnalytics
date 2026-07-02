using MongoDB.Bson;
using SatelliteData.Infrastructure.Mongo;
using Xunit;

namespace SatelliteData.UnitTests;

public sealed class MongoDateTimeMapperTests
{
    [Fact]
    public void ToLocalOffset_ConvertsUtcBsonToLocal()
    {
        var utc = new DateTime(2024, 6, 15, 12, 30, 45, DateTimeKind.Utc);
        var bson = new BsonDateTime(utc);

        var result = MongoDateTimeMapper.ToLocalOffset(bson);

        var expectedLocal = utc.ToLocalTime();
        Assert.Equal(expectedLocal, result.DateTime);
        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(expectedLocal), result.Offset);
    }

    [Fact]
    public void ToLocalOffset_InvalidDateTime_ReturnsDefault()
    {
        var result = MongoDateTimeMapper.ToLocalOffset(BsonNull.Value);
        Assert.Equal(default, result);
    }
}
