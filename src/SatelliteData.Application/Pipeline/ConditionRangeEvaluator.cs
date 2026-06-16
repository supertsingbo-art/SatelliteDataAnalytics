using System.Text.Json;
using Microsoft.Extensions.Logging;
using SatelliteData.Application.Templates;

namespace SatelliteData.Application.Pipeline;

/// <summary>
/// 基于 conditionConfig 的参数表达式与指令条件计算有效时间段。
/// </summary>
public sealed class ConditionRangeEvaluator(ILogger<ConditionRangeEvaluator> logger)
{
    public IReadOnlyList<TimeRange> EvaluateParameterRanges(
        FilterConditionConfig config,
        EffectiveWindow taskWindow,
        IReadOnlyDictionary<string, IReadOnlyList<RawSeriesPoint>> conditionSeriesByParamId)
    {
        var symbolRanges = new Dictionary<string, IReadOnlyList<TimeRange>>(StringComparer.Ordinal);
        foreach (var condition in config.Parameters)
        {
            conditionSeriesByParamId.TryGetValue(condition.ParamId, out var series);
            series ??= Array.Empty<RawSeriesPoint>();
            symbolRanges[condition.ConditionId] = ComputeLeafRanges(
                taskWindow,
                series,
                condition.Operator,
                condition.Value);
        }

        if (symbolRanges.Count == 0)
        {
            return [new TimeRange(taskWindow.Start, taskWindow.End)];
        }

        if (string.IsNullOrWhiteSpace(config.Expression))
        {
            var all = symbolRanges.Values.ToList();
            var acc = all[0];
            for (var i = 1; i < all.Count; i++)
            {
                acc = IntersectRanges(acc, all[i]);
            }

            return ClipToWindow(acc, taskWindow);
        }

        if (!ConditionExpressionParser.TryParseToPostfix(config.Expression, out var postfix, out var parseError))
        {
            throw new InvalidOperationException($"conditionConfig.expression 解析失败: {parseError}");
        }

        if (!ConditionExpressionParser.ValidateIdentifiers(postfix, symbolRanges.Keys.ToHashSet(StringComparer.Ordinal), out var idError))
        {
            throw new InvalidOperationException($"conditionConfig.expression 校验失败: {idError}");
        }

        var stack = new Stack<IReadOnlyList<TimeRange>>();
        foreach (var token in postfix)
        {
            switch (token.Type)
            {
                case ConditionTokenType.Identifier:
                    stack.Push(symbolRanges[token.Value]);
                    break;
                case ConditionTokenType.And:
                {
                    if (stack.Count < 2)
                    {
                        throw new InvalidOperationException("conditionConfig.expression 缺少操作数（&&）");
                    }

                    var right = stack.Pop();
                    var left = stack.Pop();
                    stack.Push(IntersectRanges(left, right));
                    break;
                }
                case ConditionTokenType.Or:
                {
                    if (stack.Count < 2)
                    {
                        throw new InvalidOperationException("conditionConfig.expression 缺少操作数（||）");
                    }

                    var right = stack.Pop();
                    var left = stack.Pop();
                    stack.Push(UnionRanges(left.Concat(right).ToList()));
                    break;
                }
            }
        }

        if (stack.Count != 1)
        {
            throw new InvalidOperationException("conditionConfig.expression 求值失败");
        }

        return ClipToWindow(stack.Pop(), taskWindow);
    }

    public IReadOnlyList<TimeRange> EvaluateInstructionRanges(
        FilterConditionConfig config,
        EffectiveWindow taskWindow,
        IReadOnlyList<InstructionHistoryPoint> history)
    {
        if (config.StartCommands.Count == 0 && config.EndCommands.Count == 0)
        {
            return [new TimeRange(taskWindow.Start, taskWindow.End)];
        }

        var startTimes = ResolveInstructionTimes(
            history,
            config.StartCommands,
            config.StartRelation);
        var endTimes = ResolveInstructionTimes(
            history,
            config.EndCommands,
            config.EndRelation);

        if (startTimes.Count == 0 && endTimes.Count == 0)
        {
            logger.LogWarning("指令条件配置存在，但历史数据中无匹配指令；有效时间段为空");
            return Array.Empty<TimeRange>();
        }

        var ranges = new List<TimeRange>();
        if (startTimes.Count == 0)
        {
            var firstEnd = endTimes.OrderBy(t => t).FirstOrDefault();
            if (firstEnd > taskWindow.Start)
            {
                ranges.Add(new TimeRange(taskWindow.Start, firstEnd));
            }

            return ClipToWindow(UnionRanges(ranges), taskWindow);
        }

        var sortedStarts = startTimes.OrderBy(t => t).ToArray();
        var sortedEnds = endTimes.OrderBy(t => t).ToArray();
        var endIndex = 0;
        foreach (var start in sortedStarts)
        {
            while (endIndex < sortedEnds.Length && sortedEnds[endIndex] <= start)
            {
                endIndex++;
            }

            var end = endIndex < sortedEnds.Length ? sortedEnds[endIndex] : taskWindow.End;
            if (end > start)
            {
                ranges.Add(new TimeRange(start, end));
            }
        }

        return ClipToWindow(UnionRanges(ranges), taskWindow);
    }

    private static IReadOnlyList<DateTimeOffset> ResolveInstructionTimes(
        IReadOnlyList<InstructionHistoryPoint> history,
        IReadOnlyList<InstructionConditionItem> conditions,
        string relation)
    {
        if (conditions.Count == 0)
        {
            return Array.Empty<DateTimeOffset>();
        }

        var byCommand = history
            .GroupBy(x => x.CommandId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.ExecuteTime).Select(x => x.ExecuteTime).ToArray(),
                StringComparer.Ordinal);
        var commandIds = conditions
            .Select(x => x.CommandId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (commandIds.Length == 0)
        {
            return Array.Empty<DateTimeOffset>();
        }

        if (string.Equals(relation, "AND", StringComparison.Ordinal))
        {
            // 以秒级桶近似多指令“同时成立”语义。
            var secondBuckets = new Dictionary<long, HashSet<string>>();
            foreach (var item in history.Where(x => commandIds.Contains(x.CommandId, StringComparer.Ordinal)))
            {
                var bucket = item.ExecuteTime.ToUnixTimeSeconds();
                if (!secondBuckets.TryGetValue(bucket, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    secondBuckets[bucket] = set;
                }

                set.Add(item.CommandId);
            }

            return secondBuckets
                .Where(x => commandIds.All(c => x.Value.Contains(c)))
                .Select(x => DateTimeOffset.FromUnixTimeSeconds(x.Key))
                .OrderBy(x => x)
                .ToArray();
        }

        var all = new List<DateTimeOffset>();
        foreach (var commandId in commandIds)
        {
            if (byCommand.TryGetValue(commandId, out var times))
            {
                all.AddRange(times);
            }
        }

        return all
            .Distinct()
            .OrderBy(x => x)
            .ToArray();
    }

    private static IReadOnlyList<TimeRange> ComputeLeafRanges(
        EffectiveWindow taskWindow,
        IReadOnlyList<RawSeriesPoint> series,
        string op,
        JsonElement threshold)
    {
        if (series.Count == 0)
        {
            return Array.Empty<TimeRange>();
        }

        var timeline = BuildTimeline(taskWindow, series);
        if (timeline.Count < 2)
        {
            return Array.Empty<TimeRange>();
        }

        var ranges = new List<TimeRange>();
        DateTimeOffset? segStart = null;
        for (var i = 0; i < timeline.Count; i++)
        {
            var t = timeline[i];
            var actual = InterpolateAt(series, t);
            var ok = actual is not null && Compare(op, actual.Value, threshold);
            if (ok)
            {
                segStart ??= t;
            }
            else if (segStart is not null)
            {
                ranges.Add(new TimeRange(segStart.Value, t));
                segStart = null;
            }
        }

        if (segStart is not null)
        {
            ranges.Add(new TimeRange(segStart.Value, timeline[^1]));
        }

        return ClipToWindow(ranges, taskWindow);
    }

    private static IReadOnlyList<DateTimeOffset> BuildTimeline(
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

        return set.ToArray();
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
            "=" or "==" => Math.Abs(actual - ReadNumber(threshold)) < 1e-9,
            "!=" => Math.Abs(actual - ReadNumber(threshold)) >= 1e-9,
            "between" when threshold.ValueKind == JsonValueKind.Array && threshold.GetArrayLength() >= 2 =>
                actual >= ReadNumber(threshold[0]) && actual <= ReadNumber(threshold[1]),
            _ => false
        };
    }

    private static double ReadNumber(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String when double.TryParse(element.GetString(), out var d) => d,
            _ => 0
        };
    }

    public static IReadOnlyList<TimeRange> IntersectRanges(
        IReadOnlyList<TimeRange> left,
        IReadOnlyList<TimeRange> right)
    {
        var a = left.OrderBy(x => x.Start).ToArray();
        var b = right.OrderBy(x => x.Start).ToArray();
        var result = new List<TimeRange>();
        var i = 0;
        var j = 0;
        while (i < a.Length && j < b.Length)
        {
            var start = a[i].Start > b[j].Start ? a[i].Start : b[j].Start;
            var end = a[i].End < b[j].End ? a[i].End : b[j].End;
            if (start < end)
            {
                result.Add(new TimeRange(start, end));
            }

            if (a[i].End < b[j].End)
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

    public static IReadOnlyList<TimeRange> UnionRanges(IReadOnlyList<TimeRange> ranges)
    {
        if (ranges.Count == 0)
        {
            return Array.Empty<TimeRange>();
        }

        var sorted = ranges.OrderBy(x => x.Start).ToArray();
        var merged = new List<TimeRange> { sorted[0] };
        for (var i = 1; i < sorted.Length; i++)
        {
            var last = merged[^1];
            var curr = sorted[i];
            if (curr.Start <= last.End)
            {
                if (curr.End > last.End)
                {
                    merged[^1] = new TimeRange(last.Start, curr.End);
                }
            }
            else
            {
                merged.Add(curr);
            }
        }

        return merged;
    }

    public static IReadOnlyList<TimeRange> ClipToWindow(
        IReadOnlyList<TimeRange> ranges,
        EffectiveWindow taskWindow)
    {
        var result = new List<TimeRange>();
        foreach (var range in ranges)
        {
            var start = range.Start < taskWindow.Start ? taskWindow.Start : range.Start;
            var end = range.End > taskWindow.End ? taskWindow.End : range.End;
            if (start < end)
            {
                result.Add(new TimeRange(start, end));
            }
        }

        return result;
    }
}
