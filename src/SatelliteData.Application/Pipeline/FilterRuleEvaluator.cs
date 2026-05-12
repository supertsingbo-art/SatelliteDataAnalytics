using System.Text.Json;
using Microsoft.Extensions.Logging;
using SatelliteData.Domain.Assets;

namespace SatelliteData.Application.Pipeline;

public sealed class FilterRuleEvaluator(ILogger<FilterRuleEvaluator> logger) : IFilterRuleEvaluator
{
    public Task<(EffectiveWindow Window, IReadOnlyList<TargetParamSpec> Targets)> EvaluateAsync(
        JsonElement filterConfigJson,
        string tasookNo,
        string satelliteNo,
        string? testBatchId,
        DateTimeOffset? taskWindowStart,
        DateTimeOffset? taskWindowEnd,
        IReadOnlyCollection<TestBatchCache> testBatches,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (!filterConfigJson.TryGetProperty("timeWindow", out var tw) || tw.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("筛选模板缺少 timeWindow");
        }

        var mode = tw.TryGetProperty("mode", out var modeNode) && modeNode.ValueKind == JsonValueKind.String
            ? modeNode.GetString() ?? ""
            : "";

        DateTimeOffset start;
        DateTimeOffset end;
        if (string.Equals(mode, "TEST_BATCH", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(testBatchId))
            {
                throw new InvalidOperationException("timeWindow.mode=TEST_BATCH 时需要 test_batch_id");
            }

            var batch = testBatches.FirstOrDefault(b =>
                string.Equals(b.TestBatchId, testBatchId, StringComparison.Ordinal));
            if (batch is null)
            {
                throw new InvalidOperationException($"测试阶段未缓存：{testBatchId}");
            }

            start = batch.StartTs;
            end = batch.EndTs;
            var bufBefore = ReadOptionalInt(tw, "bufferBeforeSeconds");
            var bufAfter = ReadOptionalInt(tw, "bufferAfterSeconds");
            start = start.AddSeconds(-bufBefore);
            end = end.AddSeconds(bufAfter);
        }
        else if (string.Equals(mode, "CUSTOM", StringComparison.OrdinalIgnoreCase))
        {
            if (taskWindowStart is null || taskWindowEnd is null)
            {
                throw new InvalidOperationException("timeWindow.mode=CUSTOM 时需要任务指定 window_start/window_end");
            }

            start = taskWindowStart.Value;
            end = taskWindowEnd.Value;
        }
        else
        {
            throw new InvalidOperationException($"不支持的 timeWindow.mode: {mode}");
        }

        if (!filterConfigJson.TryGetProperty("targetParams", out var tp) || tp.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("筛选模板缺少 targetParams");
        }

        var targets = new List<TargetParamSpec>();
        foreach (var item in tp.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!item.TryGetProperty("paramId", out var pid) || pid.ValueKind != JsonValueKind.String) continue;
            var id = pid.GetString();
            if (string.IsNullOrWhiteSpace(id)) continue;
            var outlierMethod = "SIGMA";
            if (item.TryGetProperty("outlier", out var outlier) && outlier.ValueKind == JsonValueKind.Object
                && outlier.TryGetProperty("method", out var om) && om.ValueKind == JsonValueKind.String)
            {
                outlierMethod = om.GetString() ?? "SIGMA";
            }

            targets.Add(new TargetParamSpec(id.Trim(), outlierMethod));
        }

        if (targets.Count == 0)
        {
            throw new InvalidOperationException("targetParams 为空");
        }

        logger.LogDebug("Effective window {Start:o} - {End:o}, targets {Count}", start, end, targets.Count);
        return Task.FromResult((new EffectiveWindow(start, end), (IReadOnlyList<TargetParamSpec>)targets));
    }

    private static int ReadOptionalInt(JsonElement tw, string name)
    {
        if (!tw.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.Number) return 0;
        return node.TryGetInt32(out var v) ? Math.Max(0, v) : 0;
    }
}
