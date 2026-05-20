using SatelliteData.Application.Pipeline;
using Xunit;

namespace SatelliteData.UnitTests;

public class DefaultOutlierDetectorTests
{
    private readonly DefaultOutlierDetector _detector = new();

    [Fact]
    public void Threshold_uses_template_min_max()
    {
        var values = new List<double> { 1, 5, 10, 15 };
        var flags = _detector.MarkOutliers(
            values,
            new OutlierDetectionOptions("THRESHOLD", Min: 2, Max: 12, SigmaK: 3, WindowSize: 30));

        Assert.Equal<byte>(1, flags[0]);
        Assert.Equal<byte>(0, flags[1]);
        Assert.Equal<byte>(0, flags[2]);
        Assert.Equal<byte>(1, flags[3]);
    }

    [Fact]
    public void Sigma_uses_template_sigma_multiplier()
    {
        var values = new List<double> { 0, 0, 0, 0, 100 };
        var flags = _detector.MarkOutliers(
            values,
            new OutlierDetectionOptions("SIGMA", null, null, SigmaK: 1.5, WindowSize: 30));

        Assert.Equal<byte>(1, flags[4]);
    }

    [Fact]
    public void Iqr_flags_extreme_values()
    {
        var values = new List<double> { 1, 2, 3, 4, 5, 6, 7, 8, 100 };
        var flags = _detector.MarkOutliers(
            values,
            new OutlierDetectionOptions("IQR", null, null, 3, 30));

        Assert.Equal<byte>(1, flags[8]);
    }
}
