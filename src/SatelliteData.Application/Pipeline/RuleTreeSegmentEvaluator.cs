using System.Text.Json;
using Microsoft.Extensions.Logging;
using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Pipeline;

/// <summary>
/// 根据 ruleTree 与条件参数时序计算有效时间段：
/// 先在各参数自身时间轴上筛出满足阈值条件的 {Tmin,Tmax} 段，再按 AND（交集）/ OR（并集）/ NOT（补集）合并。
/// </summary>
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
            logger.LogDebug(
                "ruleTree 无参数条件，有效窗使用任务数据时间范围 {Start:o}..{End:o}",
                taskWindow.Start,
                taskWindow.End);
            return [new TimeRange(taskWindow.Start, taskWindow.End)];
        }

        if (durationSeconds < 0)
        {
            durationSeconds = 0;
        }

        // 1~3：按叶子参数在任务窗内各自筛段；4：按 ruleTree 逻辑算子做区间与/或/非
        var merged = EvaluateNodeRanges(ruleTree, taskWindow, conditionSeriesByParamId);
        merged = ClipToWindow(merged, taskWindow);

        if (durationSeconds > 0)
        {
            var minSpan = TimeSpan.FromSeconds(durationSeconds);
            merged = merged.Where(r => r.End - r.Start >= minSpan).ToList();
            if (merged.Count == 0)
            {
                logger.LogWarning("无满足 durationSeconds={Duration} 的有效段", durationSeconds);
            }
        }

        return merged;
    }

    /// <summary>递归计算 ruleTree 节点对应的有效时间段集合。</summary>
    private List<TimeRange> EvaluateNodeRanges(
        JsonElement node,
        EffectiveWindow taskWindow,
        IReadOnlyDictionary<string, IReadOnlyList<RawSeriesPoint>> seriesByParamId)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        if (node.TryGetProperty("op", out var opNode) && opNode.ValueKind == JsonValueKind.String)
        {
            var op = opNode.GetString() ?? "";
            if (!node.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var childList = children.EnumerateArray().ToArray();
            return op switch
            {
                // 与：各子树有效段的交集（求交）
                "AND" => IntersectAll(
                    childList.Select(c => EvaluateNodeRanges(c, taskWindow, seriesByParamId)).ToList()),
                // 或：各子树有效段的并集（求并）
                "OR" => UnionAll(
                    childList.SelectMany(c => EvaluateNodeRanges(c, taskWindow, seriesByParamId)).ToList()),
                // 非：任务窗内对唯一子树结果取补集
                "NOT" when childList.Length > 0 => ComplementRanges(
                    EvaluateNodeRanges(childList[0], taskWindow, seriesByParamId),
                    taskWindow),
                _ => []
            };
        }

        return ComputeLeafRanges(node, taskWindow, seriesByParamId);
    }

    /// <summary>
    /// 在任务数据时间窗内，根据单参数时序筛出满足比较条件的连续时间段（可能多段）。
    /// </summary>
    private static List<TimeRange> ComputeLeafRanges(
        JsonElement leaf,
        EffectiveWindow taskWindow,
        IReadOnlyDictionary<string, IReadOnlyList<RawSeriesPoint>> seriesByParamId)
    {
        if (!leaf.TryGetProperty("paramId", out var pidNode) || pidNode.ValueKind != JsonValueKind.String)
        {
            return [];
        }

        var paramId = pidNode.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(paramId)
            || !seriesByParamId.TryGetValue(paramId.Trim(), out var series)
            || series.Count == 0)
        {
            return [];
        }

        var timeline = BuildParamTimeline(taskWindow, series);
        if (timeline.Count < 2)
        {
            return [];
        }

        var segments = new List<TimeRange>();
        DateTimeOffset? segStart = null;

        for (var i = 0; i < timeline.Count; i++)
        {
            var t = timeline[i];
            var ok = EvaluateLeafAt(leaf, t, series);
            if (ok)
            {
                segStart ??= t;
            }
            else if (segStart is not null)
            {
                // 条件在本时刻变为不满足，有效段延续到该时刻（含边界）
                segments.Add(new TimeRange(segStart.Value, t));
                segStart = null;
            }
        }

        if (segStart is not null)
        {
            segments.Add(new TimeRange(segStart.Value, timeline[^1]));
        }

        return ClipToWindow(segments, taskWindow);
    }

    private static List<DateTimeOffset> BuildParamTimeline(
        EffectiveWindow taskWindow,
        IReadOnlyList<RawSeriesPoint> series)
    {
        var set = new SortedSet<DateTimeOffset> { taskWindow.Start, taskWindow.End };
        foreach (var p in series)
        {
            if (p.Ts >= taskWindow.Start && p.Ts <= taskWindow.End)
            {
                set.Add(p.Ts);
            }
        }

        return set.ToList();
    }

    private static bool EvaluateLeafAt(
        JsonElement leaf,
        DateTimeOffset t,
        IReadOnlyList<RawSeriesPoint> series)
    {
        var value = InterpolateAt(series, t);
        if (value is null)
        {
            return false;
        }

        if (!leaf.TryGetProperty("operator", out var opNode) || opNode.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        if (!leaf.TryGetProperty("value", out var threshold))
        {
            return false;
        }

        return Compare(opNode.GetString() ?? "", value.Value, threshold);
    }

    private static List<TimeRange> IntersectAll(IReadOnlyList<List<TimeRange>> sets)
    {
        if (sets.Count == 0)
        {
            return [];
        }

        var acc = sets[0];
        for (var i = 1; i < sets.Count; i++)
        {
            acc = IntersectRanges(acc, sets[i]);
            if (acc.Count == 0)
            {
                break;
            }
        }

        return acc;
    }

    private static List<TimeRange> IntersectRanges(IReadOnlyList<TimeRange> a, IReadOnlyList<TimeRange> b)
    {
        var left = a.OrderBy(r => r.Start).ToList();
        var right = b.OrderBy(r => r.Start).ToList();
        var result = new List<TimeRange>();
        var i = 0;
        var j = 0;

        while (i < left.Count && j < right.Count)
        {
            var start = left[i].Start > right[j].Start ? left[i].Start : right[j].Start;
            var end = left[i].End < right[j].End ? left[i].End : right[j].End;
            if (start < end)
            {
                result.Add(new TimeRange(start, end));
            }

            if (left[i].End < right[j].End)
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return result;
    }

    private static List<TimeRange> UnionAll(IReadOnlyList<TimeRange> ranges)
    {
        if (ranges.Count == 0)
        {
            return [];
        }

        var sorted = ranges.OrderBy(r => r.Start).ToList();
        var merged = new List<TimeRange> { sorted[0] };
        for (var k = 1; k < sorted.Count; k++)
        {
            var last = merged[^1];
            var cur = sorted[k];
            if (cur.Start <= last.End)
            {
                if (cur.End > last.End)
                {
                    merged[^1] = new TimeRange(last.Start, cur.End);
                }
            }
            else
            {
                merged.Add(cur);
            }
        }

        return merged;
    }

    private static List<TimeRange> ComplementRanges(
        IReadOnlyList<TimeRange> ranges,
        EffectiveWindow taskWindow)
    {
        var sorted = ranges.OrderBy(r => r.Start).ToList();
        var result = new List<TimeRange>();
        var cursor = taskWindow.Start;

        foreach (var r in sorted)
        {
            if (r.End <= taskWindow.Start || r.Start >= taskWindow.End)
            {
                continue;
            }

            var segStart = r.Start < taskWindow.Start ? taskWindow.Start : r.Start;
            var segEnd = r.End > taskWindow.End ? taskWindow.End : r.End;
            if (segStart > cursor)
            {
                result.Add(new TimeRange(cursor, segStart));
            }

            if (segEnd > cursor)
            {
                cursor = segEnd;
            }
        }

        if (cursor < taskWindow.End)
        {
            result.Add(new TimeRange(cursor, taskWindow.End));
        }

        return result.Where(r => r.Start < r.End).ToList();
    }

    private static List<TimeRange> ClipToWindow(
        IReadOnlyList<TimeRange> ranges,
        EffectiveWindow taskWindow)
    {
        var result = new List<TimeRange>();
        foreach (var r in ranges)
        {
            var start = r.Start < taskWindow.Start ? taskWindow.Start : r.Start;
            var end = r.End > taskWindow.End ? taskWindow.End : r.End;
            if (start < end)
            {
                result.Add(new TimeRange(start, end));
            }
        }

        return result;
    }

    public static HashSet<string> CollectConditionParamIds(JsonElement ruleTree)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        Collect(ruleTree, set);
        return set;
    }

    public static bool HasConditionParameters(JsonElement ruleTree) =>
        CollectConditionParamIds(ruleTree).Count > 0;

    private static void Collect(JsonElement node, HashSet<string> set)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (node.TryGetProperty("paramId", out var pid) && pid.ValueKind == JsonValueKind.String)
        {
            var s = pid.GetString();
            if (!string.IsNullOrWhiteSpace(s))
            {
                set.Add(s.Trim());
            }
        }

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                Collect(child, set);
            }
        }
    }

    private static double? InterpolateAt(IReadOnlyList<RawSeriesPoint> series, DateTimeOffset t)
    {
        RawSeriesPoint? before = null;
        RawSeriesPoint? after = null;
        foreach (var p in series)
        {
            if (p.Ts <= t)
            {
                before = p;
            }

            if (p.Ts >= t)
            {
                after = p;
                break;
            }
        }

        if (before is null && after is null)
        {
            return null;
        }

        // 首采样点之前无数据，不参与条件判断
        if (before is null)
        {
            return null;
        }

        if (after is null || before.Ts == after.Ts)
        {
            return before.Value;
        }

        if (t > before.Ts && t < after.Ts)
        {
            var ratio = (t - before.Ts).TotalSeconds / (after.Ts - before.Ts).TotalSeconds;
            return before.Value + (after.Value - before.Value) * ratio;
        }

        return before.Value;
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
        if (ranges.Count == 0)
        {
            return ranges;
        }

        var before = TimeSpan.FromSeconds(Math.Max(0, bufferBeforeSec));
        var after = TimeSpan.FromSeconds(Math.Max(0, bufferAfterSec));
        return ranges
            .Select(r =>
            {
                var start = r.Start - before;
                var end = r.End + after;
                if (start < taskWindow.Start)
                {
                    start = taskWindow.Start;
                }

                if (end > taskWindow.End)
                {
                    end = taskWindow.End;
                }

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
        DateTimeOffset createdAt,
        string segmentKind = OutlierSegmentKind.Auto)
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
                    createdAt,
                    segmentKind));
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
                createdAt,
                segmentKind));
        }

        return segments;
    }
}
