using MongoDB.Bson;
using SatelliteData.Infrastructure.Mongo;
using Xunit;

namespace SatelliteData.UnitTests;

public sealed class MongoDateTimeMapperTests
{
    [Fact]
    public void ToUtcOffset_ConvertsUtcBsonToUtcOffset()
    {
        var utc = new DateTime(2024, 6, 15, 12, 30, 45, DateTimeKind.Utc);
        var bson = new BsonDateTime(utc);

        var result = MongoDateTimeMapper.ToUtcOffset(bson);

        Assert.Equal(utc, result.UtcDateTime);
        Assert.Equal(TimeSpan.Zero, result.Offset);
    }

    [Fact]
    public void ToUtcOffset_InvalidDateTime_ReturnsDefault()
    {
        var result = MongoDateTimeMapper.ToUtcOffset(BsonNull.Value);
        Assert.Equal(default, result);
    }
}
