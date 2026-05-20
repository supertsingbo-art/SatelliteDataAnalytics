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
            var bufBefore = ReadOptionalInt(tw, "bufferBeforeSeconds");
            var bufAfter = ReadOptionalInt(tw, "bufferAfterSeconds");

            if (!string.IsNullOrWhiteSpace(testBatchId))
            {
                var batch = testBatches.FirstOrDefault(b =>
                    string.Equals(b.TestBatchName, testBatchId, StringComparison.Ordinal));
                if (batch is null)
                {
                    throw new InvalidOperationException($"测试阶段未缓存：{testBatchId}");
                }

                start = batch.StartTs.AddSeconds(-bufBefore);
                end = batch.EndTs.AddSeconds(bufAfter);
            }
            else if (taskWindowStart is not null && taskWindowEnd is not null)
            {
                start = taskWindowStart.Value.AddSeconds(-bufBefore);
                end = taskWindowEnd.Value.AddSeconds(bufAfter);
            }
            else
            {
                throw new InvalidOperationException(
                    "timeWindow.mode=TEST_BATCH 时需要任务 window_start/window_end（预处理流水线不再按 test_batch_cache 反查时间窗）");
            }
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
            var outlier = OutlierDetectionOptions.Parse(item);
            var bufBefore = ReadOptionalInt(item, "boundaryBufferBeforeSec");
            var bufAfter = ReadOptionalInt(item, "boundaryBufferAfterSec");
            targets.Add(new TargetParamSpec(id.Trim(), outlier, bufBefore, bufAfter));
        }

        if (targets.Count == 0)
        {
            throw new InvalidOperationException("targetParams 为空");
        }

        logger.LogDebug("Effective window {Start:o} - {End:o}, targets {Count}", start, end, targets.Count);
        return Task.FromResult((new EffectiveWindow(start, end), (IReadOnlyList<TargetParamSpec>)targets));
    }

    private static int ReadOptionalInt(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.Number) return 0;
        return node.TryGetInt32(out var v) ? Math.Max(0, v) : 0;
    }
}
