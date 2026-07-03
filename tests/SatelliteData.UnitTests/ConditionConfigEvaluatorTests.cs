using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SatelliteData.Application.Pipeline;
using SatelliteData.Application.Templates;
using Xunit;

namespace SatelliteData.UnitTests;

public class ConditionConfigEvaluatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly EffectiveWindow Window = new(T0, T0.AddSeconds(100));
    private readonly ConditionRangeEvaluator _evaluator = new(NullLogger<ConditionRangeEvaluator>.Instance);

    [Fact]
    public void EvaluateParameterRanges_OrExpression_UnionsConditionRanges()
    {
        var config = new FilterConditionConfig(
            StartCommands: [],
            EndCommands: [],
            StartRelation: "OR",
            EndRelation: "OR",
            Parameters:
            [
                new ParameterConditionItem("P1", "A", ">", Json("5")),
                new ParameterConditionItem("P2", "B", ">", Json("5"))
            ],
            Expression: "P1 || P2");
        var conditionSeries = new Dictionary<string, IReadOnlyList<RawSeriesPoint>>(StringComparer.Ordinal)
        {
            ["A"] =
            [
                new RawSeriesPoint(T0.AddSeconds(10), 8),
                new RawSeriesPoint(T0.AddSeconds(30), 3)
            ],
            ["B"] =
            [
                new RawSeriesPoint(T0.AddSeconds(20), 7),
                new RawSeriesPoint(T0.AddSeconds(40), 2)
            ]
        };

        var ranges = _evaluator.EvaluateParameterRanges(config, Window, conditionSeries);

        Assert.Single(ranges);
        Assert.Equal(T0.AddSeconds(10), ranges[0].Start);
        Assert.Equal(T0.AddSeconds(40), ranges[0].End);
    }

    [Fact]
    public void EvaluateParameterRanges_EqualOperator_IsSupported()
    {
        var config = new FilterConditionConfig(
            StartCommands: [],
            EndCommands: [],
            StartRelation: "OR",
            EndRelation: "OR",
            Parameters:
            [
                new ParameterConditionItem("P1", "A", "=", Json("3"))
            ],
            Expression: "P1");
        var conditionSeries = new Dictionary<string, IReadOnlyList<RawSeriesPoint>>(StringComparer.Ordinal)
        {
            ["A"] =
            [
                new RawSeriesPoint(T0.AddSeconds(10), 3),
                new RawSeriesPoint(T0.AddSeconds(20), 3),
                new RawSeriesPoint(T0.AddSeconds(30), 4)
            ]
        };

        var ranges = _evaluator.EvaluateParameterRanges(config, Window, conditionSeries);

        Assert.Single(ranges);
        Assert.Equal(T0.AddSeconds(10), ranges[0].Start);
        Assert.Equal(T0.AddSeconds(30), ranges[0].End);
    }

    [Fact]
    public void EvaluateInstructionRanges_StartAndEndCommands_BuildsPairedRanges()
    {
        var config = new FilterConditionConfig(
            StartCommands:
            [
                new InstructionConditionItem("S1", "1001", 0)
            ],
            EndCommands:
            [
                new InstructionConditionItem("E1", "1002", 0)
            ],
            StartRelation: "OR",
            EndRelation: "OR",
            Parameters: [],
            Expression: string.Empty);
        var history = new List<InstructionHistoryPoint>
        {
            new("1001", 1001, 0, T0.AddSeconds(10)),
            new("1002", 1002, 0, T0.AddSeconds(20)),
            new("1001", 1001, 0, T0.AddSeconds(40)),
            new("1002", 1002, 0, T0.AddSeconds(60))
        };

        var ranges = _evaluator.EvaluateInstructionRanges(config, Window, history);

        Assert.Equal(2, ranges.Count);
        Assert.Equal(T0.AddSeconds(10), ranges[0].Start);
        Assert.Equal(T0.AddSeconds(20), ranges[0].End);
        Assert.Equal(T0.AddSeconds(40), ranges[1].Start);
        Assert.Equal(T0.AddSeconds(60), ranges[1].End);
    }

    [Fact]
    public void EvaluateInstructionRanges_AndRelationWithRange_UsesLastEventAsTrigger()
    {
        var config = new FilterConditionConfig(
            StartCommands:
            [
                new InstructionConditionItem("S1", "1001", 0),
                new InstructionConditionItem("S2", "1002", 0)
            ],
            EndCommands: [],
            StartRelation: "AND",
            EndRelation: "OR",
            Parameters: [],
            Expression: string.Empty,
            StartRangeSeconds: 5);
        var history = new List<InstructionHistoryPoint>
        {
            new("1001", 1001, 0, T0.AddSeconds(10)),
            new("1002", 1002, 0, T0.AddSeconds(14)),
            new("1001", 1001, 0, T0.AddSeconds(26)),
            new("1002", 1002, 0, T0.AddSeconds(40))
        };

        var ranges = _evaluator.EvaluateInstructionRanges(config, Window, history);

        Assert.Single(ranges);
        // 起始触发时刻取窗口内最后事件时间（14s）
        Assert.Equal(T0.AddSeconds(14), ranges[0].Start);
        Assert.Equal(Window.End, ranges[0].End);
    }

    [Fact]
    public void EvaluateInstructionRanges_Disabled_IgnoresInstructionConditions()
    {
        var config = new FilterConditionConfig(
            StartCommands:
            [
                new InstructionConditionItem("S1", "1001", 0)
            ],
            EndCommands:
            [
                new InstructionConditionItem("E1", "1002", 0)
            ],
            StartRelation: "OR",
            EndRelation: "OR",
            Parameters: [],
            Expression: string.Empty,
            InstructionsEnabled: false);

        var ranges = _evaluator.EvaluateInstructionRanges(config, Window, Array.Empty<InstructionHistoryPoint>());

        Assert.Single(ranges);
        Assert.Equal(Window.Start, ranges[0].Start);
        Assert.Equal(Window.End, ranges[0].End);
    }

    [Fact]
    public void EvaluateParameterRanges_Disabled_IgnoresParameterConditions()
    {
        var config = new FilterConditionConfig(
            StartCommands: [],
            EndCommands: [],
            StartRelation: "OR",
            EndRelation: "OR",
            Parameters:
            [
                new ParameterConditionItem("P1", "A", ">", Json("5"))
            ],
            Expression: "P1",
            ParametersEnabled: false);

        var ranges = _evaluator.EvaluateParameterRanges(
            config,
            Window,
            new Dictionary<string, IReadOnlyList<RawSeriesPoint>>(StringComparer.Ordinal));

        Assert.Single(ranges);
        Assert.Equal(Window.Start, ranges[0].Start);
        Assert.Equal(Window.End, ranges[0].End);
    }

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();
}
