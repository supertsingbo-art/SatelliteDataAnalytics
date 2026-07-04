using System.Text.Json;
using SatelliteData.Application.Algorithms;
using Xunit;

namespace SatelliteData.UnitTests;

public sealed class BuiltinAlgorithmEngineTests
{
    private static JsonElement EmptyData()
    {
        using var d = JsonDocument.Parse("{}");
        return d.RootElement.Clone();
    }

    [Fact]
    public void Max_Empty_Returns_Null_Scalar()
    {
        var o = BuiltinAlgorithmEngine.Execute("max", new NodeOutput { Series = [] }, EmptyData());
        Assert.NotNull(o);
        Assert.Null(o!.Scalar);
    }

    [Fact]
    public void Max_Values_Returns_Max()
    {
        var series = new List<(DateTimeOffset Ts, double V)>
        {
            (DateTimeOffset.Parse("2026-01-01T00:00:00Z"), 1),
            (DateTimeOffset.Parse("2026-01-01T00:00:01Z"), 5),
            (DateTimeOffset.Parse("2026-01-01T00:00:02Z"), 3)
        };
        var o = BuiltinAlgorithmEngine.Execute("max", new NodeOutput { Series = series }, EmptyData());
        Assert.Equal(5, o!.Scalar);
    }

    [Fact]
    public void Mean_SinglePoint_Returns_Value()
    {
        var series = new List<(DateTimeOffset Ts, double V)> { (DateTimeOffset.UtcNow, 42) };
        var o = BuiltinAlgorithmEngine.Execute("mean", new NodeOutput { Series = series }, EmptyData());
        Assert.Equal(42, o!.Scalar);
    }

    [Fact]
    public void Variance_Needs_Ddof_Returns_Null_When_Too_Few()
    {
        var series = new List<(DateTimeOffset Ts, double V)> { (DateTimeOffset.UtcNow, 1) };
        var o = BuiltinAlgorithmEngine.Execute("variance", new NodeOutput { Series = series }, EmptyData());
        Assert.Null(o!.Scalar);
    }

    [Fact]
    public void SaveResult_Passthrough_Scalar()
    {
        var input = new NodeOutput { Scalar = 12.5 };
        var o = BuiltinAlgorithmEngine.Execute("save_result", input, EmptyData());
        Assert.Equal(12.5, o!.Scalar);
    }

    [Fact]
    public void SaveResult_Passthrough_Series()
    {
        var series = new List<(DateTimeOffset Ts, double V)> { (DateTimeOffset.UtcNow, 3) };
        var input = new NodeOutput { Series = series };
        var o = BuiltinAlgorithmEngine.Execute("save_result", input, EmptyData());
        Assert.Single(o!.Series!);
    }

    [Fact]
    public void ThresholdJudge_Marks_Out_Of_Range()
    {
        using var doc = JsonDocument.Parse("{\"params\":{\"min\":0,\"max\":10}}");
        var series = new List<(DateTimeOffset Ts, double V)>
        {
            (DateTimeOffset.Parse("2026-01-01T00:00:00Z"), 5),
            (DateTimeOffset.Parse("2026-01-01T00:00:01Z"), 20)
        };
        var o = BuiltinAlgorithmEngine.Execute("threshold_judge", new NodeOutput { Series = series }, doc.RootElement);
        Assert.NotNull(o!.Series);
        Assert.Equal(1, o.Series![0].V);
        Assert.Equal(0, o.Series[1].V);
    }
}
