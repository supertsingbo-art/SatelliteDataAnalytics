using SatelliteData.Application.Tasks;
using Xunit;

namespace SatelliteData.UnitTests;

public sealed class ProcessedSeriesBucketCalculatorTests
{
    [Fact]
    public void ComputeBucketSeconds_UsesCeilAndMinimumOne()
    {
        var start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
        var end = start.AddSeconds(6000);

        Assert.Equal(2, ProcessedSeriesBucketCalculator.ComputeBucketSeconds(start, end, 3000));
        Assert.Equal(2, ProcessedSeriesBucketCalculator.ComputeBucketSeconds(start, end.AddSeconds(-1), 3000));
        Assert.Equal(1, ProcessedSeriesBucketCalculator.ComputeBucketSeconds(start, start, 3000));
        Assert.Equal(1, ProcessedSeriesBucketCalculator.ComputeBucketSeconds(start, start.AddSeconds(1), 3000));
    }

    [Fact]
    public void ComputeBucketSeconds_RespectsMaxPoints()
    {
        var start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
        var end = start.AddSeconds(10_000);

        Assert.Equal(4, ProcessedSeriesBucketCalculator.ComputeBucketSeconds(start, end, 3000));
        Assert.Equal(20, ProcessedSeriesBucketCalculator.ComputeBucketSeconds(start, end, 500));
    }
}
