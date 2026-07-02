using SatelliteData.Infrastructure.PostgreSql;
using Xunit;

namespace SatelliteData.UnitTests;

public sealed class PgTimestamptzTests
{
    [Fact]
    public void Utc_ConvertsNonZeroOffsetToUtc()
    {
        var local = new DateTimeOffset(2024, 6, 15, 8, 0, 0, TimeSpan.FromHours(8));
        var utc = PgTimestamptz.Utc(local);
        Assert.Equal(TimeSpan.Zero, utc.Offset);
        Assert.Equal(new DateTimeOffset(2024, 6, 15, 0, 0, 0, TimeSpan.Zero), utc);
    }

    [Fact]
    public void UtcOrDbNull_Null_ReturnsDbNull()
    {
        Assert.Equal(DBNull.Value, PgTimestamptz.UtcOrDbNull(null));
    }
}
