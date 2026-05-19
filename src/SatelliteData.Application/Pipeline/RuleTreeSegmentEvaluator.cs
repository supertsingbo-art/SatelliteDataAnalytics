using System.Text.Json;
using Microsoft.Extensions.Logging;
using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Pipeline;

/// <summary>根据 ruleTree 与条件参数时序，计算满足持续时间的最小有效时间段。</summary>
public sealed class RuleTreeSegmentEvaluator(ILogger<RuleTreeSegmentEvaluator> logger)
{
    public IReadOnlyList<TimeRange> ComputeValidRanges(
        JsonElement ruleTree,
        int durationSeconds,
        EffectiveWindow taskWindow,
        IReadOnlyDictionary<string, IReadOnlyList<RawSeriesPoint>> conditionSeriesByParamId)
    {
        if (!HasConditionParameters(ruleTree))
        {
            logger.LogDebug("ruleTree 无参数条件，有效窗使用任务数据时间范围 {Start:o}..{End:o}",
                taskWindow.Start,
                taskWindow.End);
            return [new TimeRange(taskWindow.Start, taskWindow.End)];
        }

        if (durationSeconds < 0)
        {
            durationSeconds = 0;
        }

        var timeline = BuildTimeline(taskWindow, conditionSeriesByParamId);
        if (timeline.Count == 0)
        {
            logger.LogWarning("ruleTree 评估时间轴为空，回退为任务全窗");
            return [new TimeRange(taskWindow.Start, taskWindow.End)];
        }

        var trueSegments = new List<TimeRange>();
        DateTimeOffset? segStart = null;
        for (var i = 0; i < timeline.Count; i++)
        {
            var t = timeline[i];
            var ok = EvaluateNode(ruleTree, t, conditionSeriesByParamId);
            if (ok)
            {
                segStart ??= t;
            }
            else if (segStart is not null)
            {
                var segEnd = i > 0 ? timeline[i - 1] : t;
                trueSegments.Add(new TimeRange(segStart.Value, segEnd));
                segStart = null;
            }
        }

        if (segStart is not null)
        {
            trueSegments.Add(new TimeRange(segStart.Value, timeline[^1]));
        }

        if (durationSeconds == 0)
        {
            return trueSegments.Count == 0
                ? [new TimeRange(taskWindow.Start, taskWindow.End)]
                : trueSegments;
        }

        var minSpan = TimeSpan.FromSeconds(durationSeconds);
        var filtered = trueSegments
            .Where(r => r.End - r.Start >= minSpan)
            .ToList();

        if (filtered.Count == 0)
        {
            logger.LogWarning("无满足 durationSeconds={Duration} 的有效段", durationSeconds);
        }

        return filtered;
    }

    public static HashSet<string> CollectConditionParamIds(JsonElement ruleTree)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        Collect(ruleTree, set);
        return set;
    }

    /// <summary>ruleTree 是否包含参数条件叶子；无参数时表示目标参数使用任务数据时间窗。</summary>
    public static bool HasConditionParameters(JsonElement ruleTree) =>
        CollectConditionParamIds(ruleTree).Count > 0;

    private static void Collect(JsonElement node, HashSet<string> set)
    {
        if (node.ValueKind != JsonValueKind.Object) return;
        if (node.TryGetProperty("paramId", out var pid) && pid.ValueKind == JsonValueKind.String)
        {
            var s = pid.GetString();
            if (!string.IsNullOrWhiteSpace(s)) set.Add(s.Trim());
        }

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                Collect(child, set);
            }
        }
    }

    private static List<DateTimeOffset> BuildTimeline(
        EffectiveWindow taskWindow,
        IReadOnlyDictionary<string, IReadOnlyList<RawSeriesPoint>> seriesByParamId)
    {
        var set = new SortedSet<DateTimeOffset>();
        var span = taskWindow.End - taskWindow.Start;
        var step = span.TotalSeconds <= 1 ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(1);
        for (var t = taskWindow.Start; t <= taskWindow.End; t += step)
        {
            set.Add(t);
        }

        foreach (var series in seriesByParamId.Values)
        {
            foreach (var p in series)
            {
                if (p.Ts >= taskWindow.Start && p.Ts <= taskWindow.End)
                {
                    set.Add(p.Ts);
                }
            }
        }

        return set.ToList();
    }

    private static bool EvaluateNode(
        JsonElement node,
        DateTimeOffset t,
        IReadOnlyDictionary<string, IReadOnlyList<RawSeriesPoint>> seriesByParamId)
    {
        if (node.ValueKind != JsonValueKind.Object) return false;

        if (node.TryGetProperty("op", out var opNode) && opNode.ValueKind == JsonValueKind.String)
        {
            var op = opNode.GetString() ?? "";
            if (!node.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var childResults = children.EnumerateArray()
                .Select(c => EvaluateNode(c, t, seriesByParamId))
                .ToArray();

            return op switch
            {
                "AND" => childResults.All(x => x),
                "OR" => childResults.Any(x => x),
                "NOT" => childResults.Length > 0 && !childResults[0],
                _ => false
            };
        }

        if (!node.TryGetProperty("paramId", out var pidNode) || pidNode.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var paramId = pidNode.GetString() ?? "";
        if (!seriesByParamId.TryGetValue(paramId, out var series) || series.Count == 0)
        {
            return false;
        }

        var value = InterpolateAt(series, t);
        if (value is null) return false;

        if (!node.TryGetProperty("operator", out var op2) || op2.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var opStr = op2.GetString() ?? "";
        if (!node.TryGetProperty("value", out var threshold))
        {
            return false;
        }

        return Compare(opStr, value.Value, threshold);
    }

    private static double? InterpolateAt(IReadOnlyList<RawSeriesPoint> series, DateTimeOffset t)
    {
        RawSeriesPoint? before = null;
        RawSeriesPoint? after = null;
        foreach (var p in series)
        {
            if (p.Ts <= t) before = p;
            if (p.Ts >= t)
            {
                after = p;
                break;
            }
        }

        if (before is null && after is null) return null;
        if (before is not null && (after is null || before.Ts == t)) return before.Value;
        if (after is not null && before is null) return after.Value;
        if (before!.Ts == after!.Ts) return before.Value;
        var ratio = (t - before.Ts).TotalSeconds / (after.Ts - before.Ts).TotalSeconds;
        return before.Value + (after.Value - before.Value) * ratio;
    }

    private static bool Compare(string op, double actual, JsonElement threshold)
    {
        return op switch
        {
            ">" => actual > ReadNumber(threshold),
            ">=" => actual >= ReadNumber(threshold),
            "<" => actual < ReadNumber(threshold),
            "<=" => actual <= ReadNumber(threshold),
            "==" => Math.Abs(actual - ReadNumber(threshold)) < 1e-9,
            "!=" => Math.Abs(actual - ReadNumber(threshold)) >= 1e-9,
            "between" when threshold.ValueKind == JsonValueKind.Array && threshold.GetArrayLength() >= 2 =>
                actual >= ReadNumber(threshold[0]) && actual <= ReadNumber(threshold[1]),
            _ => false
        };
    }

    private static double ReadNumber(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.Number => el.GetDouble(),
            JsonValueKind.String when double.TryParse(el.GetString(), out var d) => d,
            _ => 0
        };

    public static IReadOnlyList<TimeRange> ApplyBuffer(
        IReadOnlyList<TimeRange> ranges,
        EffectiveWindow taskWindow,
        int bufferBeforeSec,
        int bufferAfterSec)
    {
        if (ranges.Count == 0) return ranges;
        var before = TimeSpan.FromSeconds(Math.Max(0, bufferBeforeSec));
        var after = TimeSpan.FromSeconds(Math.Max(0, bufferAfterSec));
        return ranges
            .Select(r =>
            {
                var start = r.Start - before;
                var end = r.End + after;
                if (start < taskWindow.Start) start = taskWindow.Start;
                if (end > taskWindow.End) end = taskWindow.End;
                return start < end ? new TimeRange(start, end) : null;
            })
            .Where(r => r is not null)
            .Cast<TimeRange>()
            .ToList();
    }

    public static IReadOnlyList<PreprocessOutlierSegment> MergeOutlierSegments(
        Guid runId,
        string tasookNo,
        string satelliteNo,
        string paramId,
        IReadOnlyList<RawSeriesPoint> points,
        IReadOnlyList<byte> flags,
        string outlierMethod,
        DateTimeOffset createdAt)
    {
        var segments = new List<PreprocessOutlierSegment>();
        DateTimeOffset? start = null;
        for (var i = 0; i < points.Count; i++)
        {
            if (flags[i] != 0)
            {
                start ??= points[i].Ts;
            }
            else if (start is not null)
            {
                segments.Add(new PreprocessOutlierSegment(
                    Guid.NewGuid(),
                    runId,
                    tasookNo,
                    satelliteNo,
                    paramId,
                    start.Value,
                    points[i - 1].Ts,
                    outlierMethod,
                    createdAt));
                start = null;
            }
        }

        if (start is not null && points.Count > 0)
        {
            segments.Add(new PreprocessOutlierSegment(
                Guid.NewGuid(),
                runId,
                tasookNo,
                satelliteNo,
                paramId,
                start.Value,
                points[^1].Ts,
                outlierMethod,
                createdAt));
        }

        return segments;
    }
}
