using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SatelliteData.Application.Pipeline;
using Xunit;

namespace SatelliteData.UnitTests;

public class RuleTreeSegmentEvaluatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly EffectiveWindow Window = new(T0, T0.AddSeconds(100));
    private readonly RuleTreeSegmentEvaluator _evaluator = new(NullLogger<RuleTreeSegmentEvaluator>.Instance);

    [Fact]
    public void ComputeValidRanges_AND_intersects_per_parameter_segments()
    {
        var ruleTree = JsonDocument.Parse("""
            {
              "op": "AND",
              "children": [
                { "paramId": "A", "operator": ">", "value": 5 },
                { "paramId": "B", "operator": "<", "value": 80 }
              ]
            }
            """).RootElement;

        var series = new Dictionary<string, IReadOnlyList<RawSeriesPoint>>(StringComparer.Ordinal)
        {
            ["A"] =
            [
                new(T0.AddSeconds(10), 6),
                new(T0.AddSeconds(40), 6),
                new(T0.AddSeconds(50), 2)
            ],
            ["B"] =
            [
                new(T0.AddSeconds(20), 70),
                new(T0.AddSeconds(60), 70),
                new(T0.AddSeconds(70), 90)
            ]
        };

        var ranges = _evaluator.ComputeValidRanges(ruleTree, durationSeconds: 0, Window, series);

        Assert.Single(ranges);
        Assert.Equal(T0.AddSeconds(20), ranges[0].Start);
        Assert.Equal(T0.AddSeconds(50), ranges[0].End);
    }

    [Fact]
    public void ComputeValidRanges_OR_unions_per_parameter_segments()
    {
        var ruleTree = JsonDocument.Parse("""
            {
              "op": "OR",
              "children": [
                { "paramId": "A", "operator": ">", "value": 5 },
                { "paramId": "B", "operator": ">", "value": 5 }
              ]
            }
            """).RootElement;

        var series = new Dictionary<string, IReadOnlyList<RawSeriesPoint>>(StringComparer.Ordinal)
        {
            ["A"] =
            [
                new(T0.AddSeconds(10), 6),
                new(T0.AddSeconds(25), 2)
            ],
            ["B"] =
            [
                new(T0.AddSeconds(50), 8),
                new(T0.AddSeconds(65), 1)
            ]
        };

        var ranges = _evaluator.ComputeValidRanges(ruleTree, durationSeconds: 0, Window, series);

        Assert.Equal(2, ranges.Count);
        Assert.Equal(T0.AddSeconds(10), ranges[0].Start);
        Assert.Equal(T0.AddSeconds(25), ranges[0].End);
        Assert.Equal(T0.AddSeconds(50), ranges[1].Start);
        Assert.Equal(T0.AddSeconds(65), ranges[1].End);
    }

    [Fact]
    public void ComputeValidRanges_filters_by_duration_on_merged_ranges()
    {
        var ruleTree = JsonDocument.Parse("""
            { "paramId": "A", "operator": ">", "value": 0 }
            """).RootElement;

        var series = new Dictionary<string, IReadOnlyList<RawSeriesPoint>>(StringComparer.Ordinal)
        {
            ["A"] =
            [
                new(T0.AddSeconds(10), 1),
                new(T0.AddSeconds(15), 1),
                new(T0.AddSeconds(20), 0)
            ]
        };

        var ranges = _evaluator.ComputeValidRanges(ruleTree, durationSeconds: 20, Window, series);

        Assert.Empty(ranges);
    }
}
