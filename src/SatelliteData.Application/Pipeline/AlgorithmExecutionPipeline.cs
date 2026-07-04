using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SatelliteData.Application.Algorithms;
using SatelliteData.Application.Tasks;
using SatelliteData.Application.Templates;
using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Pipeline;

public sealed class AlgorithmExecutionPipeline(
    ITaskRunRepository taskRuns,
    ITaskEventRepository taskEvents,
    IAlgorithmTemplateRepository algorithmTemplates,
    IClickHouseGateway clickHouse,
    IBackgroundJobScheduler scheduler,
    ILogger<AlgorithmExecutionPipeline> logger) : IAlgorithmExecutionPipeline
{
    public async Task ExecuteAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await taskRuns.GetByRunIdAsync(runId, cancellationToken)
            ?? throw new InvalidOperationException($"task_run 不存在：{runId}");
        if (run.Status == TaskRunStatus.Cancelled) return;

        if (run.JobType != TaskJobType.Pipeline)
        {
            logger.LogDebug("Skipping algorithm pipeline for run {RunId} job_type={JobType}", runId, run.JobType);
            return;
        }

        try
        {
            await ExecuteCoreAsync(runId, run, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
    }

    private async Task ExecuteCoreAsync(Guid runId, TaskRun run, CancellationToken cancellationToken)
    {
        await TaskRunCancellation.ThrowIfCancelledAsync(taskRuns, runId, cancellationToken).ConfigureAwait(false);

        run = run with { CurrentStep = "algorithm", ProgressPercent = TaskProgressBands.AlgorithmMax - 15m };
        if (!await taskRuns.UpdateIfNotCancelledAsync(run, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (run.AlgorithmTemplateId is null || run.AlgorithmTemplateVersion is null)
        {
            await FailAsync(run, "ALG_001", "缺少算法模板", cancellationToken);
            return;
        }

        var template = await algorithmTemplates.GetVersionAsync(
            run.AlgorithmTemplateId.Value,
            run.AlgorithmTemplateVersion.Value,
            cancellationToken);
        if (template is null)
        {
            await FailAsync(run, "ALG_002", "算法模板版本不存在", cancellationToken);
            return;
        }

        var nodes = AlgorithmReactFlowParser.ParseNodes(template.ReactFlowJson);
        var edges = AlgorithmReactFlowParser.ParseEdges(template.ReactFlowJson);
        IReadOnlyList<string> order;
        try
        {
            order = AlgorithmReactFlowParser.TopologicalSort(nodes, edges);
        }
        catch (Exception ex)
        {
            await FailAsync(run, "ALG_003", ex.Message, cancellationToken);
            return;
        }

        var nodeMap = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var preds = edges.GroupBy(e => e.Target, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Source).FirstOrDefault(), StringComparer.Ordinal);

        var outputs = new Dictionary<string, NodeOutput>(StringComparer.Ordinal);
        var batchId = run.TestBatchName ?? "default";
        var winStart = run.WindowStart ?? DateTimeOffset.MinValue;
        var winEnd = run.WindowEnd ?? DateTimeOffset.MaxValue;

        foreach (var nodeId in order)
        {
            await TaskRunCancellation.ThrowIfCancelledAsync(taskRuns, runId, cancellationToken)
                .ConfigureAwait(false);

            var node = nodeMap[nodeId];
            preds.TryGetValue(nodeId, out var predId);
            outputs.TryGetValue(predId ?? "", out var input);

            var cat = node.Type.ToLowerInvariant();
            if (cat == "source")
            {
                var paramId = ReadParamId(node.Data);
                if (string.IsNullOrEmpty(paramId))
                {
                    await FailAsync(run, "ALG_004", $"source 节点缺少 paramId：{nodeId}", cancellationToken);
                    return;
                }

                var pts = await clickHouse.QueryProcessedSeriesAsync(
                    run.TasookNo,
                    run.SatelliteNo,
                    batchId,
                    paramId,
                    winStart,
                    winEnd,
                    cancellationToken);
                outputs[nodeId] = new NodeOutput { Series = pts.ToList() };
                continue;
            }

            if (string.IsNullOrEmpty(node.AlgorithmCode))
            {
                await FailAsync(run, "ALG_005", $"节点缺少 algorithmCode：{nodeId}", cancellationToken);
                return;
            }

            var outv = BuiltinAlgorithmEngine.Execute(node.AlgorithmCode, input, node.Data);
            if (outv is null)
            {
                await FailAsync(run, "ALG_006", $"不支持的算法：{node.AlgorithmCode}", cancellationToken);
                return;
            }

            outputs[nodeId] = outv;
        }

        var algoRows = new List<string>();
        foreach (var nodeId in order)
        {
            var node = nodeMap[nodeId];
            if (!outputs.TryGetValue(nodeId, out var o)) continue;
            if (node.Type.Equals("source", StringComparison.OrdinalIgnoreCase)) continue;

            preds.TryGetValue(nodeId, out var predId);
            nodeMap.TryGetValue(predId ?? "", out var upstream);

            if (string.Equals(node.AlgorithmCode, "save_result", StringComparison.OrdinalIgnoreCase))
            {
                AppendSaveResultRow(algoRows, run, nodeId, node, upstream, o, batchId, winStart, winEnd);
                continue;
            }

            var isJudge = string.Equals(node.AlgorithmCode, "threshold_judge", StringComparison.OrdinalIgnoreCase)
                || string.Equals(node.AlgorithmCode, "three_sigma_judge", StringComparison.OrdinalIgnoreCase);
            if (isJudge && o.Series is { Count: > 0 } seriesPoints)
            {
                var detail = JsonSerializer.Serialize(seriesPoints.Select(p => new { ts = p.Ts, flag = p.V }));
                algoRows.Add(JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["run_id"] = runId.ToString(),
                    ["node_id"] = nodeId,
                    ["algorithm_code"] = node.AlgorithmCode ?? "",
                    ["tasook_no"] = run.TasookNo,
                    ["satellite_no"] = run.SatelliteNo,
                    ["test_batch_id"] = batchId,
                    ["window_start"] = (run.WindowStart ?? winStart).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                    ["window_end"] = (run.WindowEnd ?? winEnd).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                    ["metric_name"] = "judge",
                    ["metric_value"] = seriesPoints.Average(x => x.V),
                    ["detail_json"] = detail
                }));
            }
        }

        if (algoRows.Count > 0)
        {
            await TaskRunCancellation.ThrowIfCancelledAsync(taskRuns, runId, cancellationToken)
                .ConfigureAwait(false);
            await clickHouse.EnsureAlgoResultTableAsync(cancellationToken).ConfigureAwait(false);
            await clickHouse.InsertJsonEachRowAsync("algo_result", algoRows, cancellationToken);
        }

        if (await TaskRunCancellation.IsCancelledAsync(taskRuns, runId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var end = DateTimeOffset.UtcNow;
        run = (await taskRuns.GetByRunIdAsync(runId, cancellationToken))!;
        if (run.Status == TaskRunStatus.Cancelled)
        {
            return;
        }

        run = run with
        {
            Status = TaskRunStatus.Succeeded,
            ProgressPercent = TaskProgressBands.WebhookMax - 1m,
            CurrentStep = "algorithm_done",
            EndTime = end
        };
        if (!await taskRuns.UpdateIfNotCancelledAsync(run, cancellationToken).ConfigureAwait(false))
        {
            return;
        }
        await taskEvents.AppendAsync(
            new TaskEvent(
                Guid.NewGuid(),
                runId,
                "task.succeeded",
                "Succeeded",
                null,
                null,
                null,
                end),
            cancellationToken);

        logger.LogInformation("Algorithm finished for run {RunId}", runId);
        scheduler.EnqueueWebhook(runId);
    }

    private static void AppendSaveResultRow(
        List<string> algoRows,
        TaskRun run,
        string nodeId,
        FlowNodeRef node,
        FlowNodeRef? upstream,
        NodeOutput output,
        string batchId,
        DateTimeOffset winStart,
        DateTimeOffset winEnd)
    {
        var metricName = ReadMetricName(node.Data, upstream);
        var includeDetail = ReadIncludeDetail(node.Data);
        var windowStart = (run.WindowStart ?? winStart).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var windowEnd = (run.WindowEnd ?? winEnd).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

        if (output.Scalar.HasValue)
        {
            algoRows.Add(JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["run_id"] = run.RunId.ToString(),
                ["node_id"] = nodeId,
                ["algorithm_code"] = upstream?.AlgorithmCode ?? node.AlgorithmCode ?? "",
                ["tasook_no"] = run.TasookNo,
                ["satellite_no"] = run.SatelliteNo,
                ["test_batch_id"] = batchId,
                ["window_start"] = windowStart,
                ["window_end"] = windowEnd,
                ["metric_name"] = metricName,
                ["metric_value"] = output.Scalar.Value,
                ["detail_json"] = "{}"
            }));
            return;
        }

        if (output.Series is { Count: > 0 } seriesPoints)
        {
            var detail = includeDetail
                ? JsonSerializer.Serialize(seriesPoints.Select(p => new { ts = p.Ts, value = p.V }))
                : "{}";
            algoRows.Add(JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["run_id"] = run.RunId.ToString(),
                ["node_id"] = nodeId,
                ["algorithm_code"] = upstream?.AlgorithmCode ?? node.AlgorithmCode ?? "",
                ["tasook_no"] = run.TasookNo,
                ["satellite_no"] = run.SatelliteNo,
                ["test_batch_id"] = batchId,
                ["window_start"] = windowStart,
                ["window_end"] = windowEnd,
                ["metric_name"] = metricName,
                ["metric_value"] = seriesPoints.Average(x => x.V),
                ["detail_json"] = detail
            }));
            return;
        }

        if (output.Spectrum is { Magnitudes.Length: > 0 } spectrum)
        {
            var peakIdx = 0;
            var peakMag = spectrum.Magnitudes[0];
            for (var i = 1; i < spectrum.Magnitudes.Length; i++)
            {
                if (spectrum.Magnitudes[i] > peakMag)
                {
                    peakMag = spectrum.Magnitudes[i];
                    peakIdx = i;
                }
            }

            var peakFreq = spectrum.Frequencies.Length > peakIdx ? spectrum.Frequencies[peakIdx] : 0d;
            var detail = includeDetail
                ? JsonSerializer.Serialize(new
                {
                    frequencies = spectrum.Frequencies,
                    magnitudes = spectrum.Magnitudes,
                    peakFrequency = peakFreq,
                    peakMagnitude = peakMag
                })
                : JsonSerializer.Serialize(new { peakFrequency = peakFreq, peakMagnitude = peakMag });

            algoRows.Add(JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["run_id"] = run.RunId.ToString(),
                ["node_id"] = nodeId,
                ["algorithm_code"] = upstream?.AlgorithmCode ?? node.AlgorithmCode ?? "",
                ["tasook_no"] = run.TasookNo,
                ["satellite_no"] = run.SatelliteNo,
                ["test_batch_id"] = batchId,
                ["window_start"] = windowStart,
                ["window_end"] = windowEnd,
                ["metric_name"] = metricName,
                ["metric_value"] = peakMag,
                ["detail_json"] = detail
            }));
        }
    }

    private static string ReadMetricName(JsonElement nodeData, FlowNodeRef? upstream)
    {
        if (TryReadParams(nodeData, out var p)
            && p.TryGetProperty("metricName", out var mn)
            && mn.ValueKind == JsonValueKind.String)
        {
            var name = mn.GetString()?.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }
        }

        if (upstream?.Data.ValueKind == JsonValueKind.Object
            && upstream.Data.TryGetProperty("displayName", out var dn)
            && dn.ValueKind == JsonValueKind.String)
        {
            var display = dn.GetString()?.Trim();
            if (!string.IsNullOrEmpty(display))
            {
                return display;
            }
        }

        return upstream?.AlgorithmCode ?? "result";
    }

    private static bool ReadIncludeDetail(JsonElement nodeData)
    {
        if (!TryReadParams(nodeData, out var p)
            || !p.TryGetProperty("includeDetail", out var flag))
        {
            return true;
        }

        return flag.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => true
        };
    }

    private static bool TryReadParams(JsonElement nodeData, out JsonElement p)
    {
        p = default;
        if (nodeData.ValueKind != JsonValueKind.Object) return false;
        if (nodeData.TryGetProperty("params", out p) && p.ValueKind == JsonValueKind.Object) return true;
        return nodeData.TryGetProperty("paramsValues", out p) && p.ValueKind == JsonValueKind.Object;
    }

    private static string ReadParamId(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object) return "";
        if (data.TryGetProperty("paramId", out var p) && p.ValueKind == JsonValueKind.String) return p.GetString() ?? "";
        return "";
    }

    private async Task FailAsync(TaskRun run, string code, string message, CancellationToken cancellationToken)
    {
        if (await TaskRunCancellation.IsCancelledAsync(taskRuns, run.RunId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var end = DateTimeOffset.UtcNow;
        if (!await taskRuns.UpdateIfNotCancelledAsync(
            run with
            {
                Status = TaskRunStatus.Failed,
                ErrorCode = code,
                ErrorMsg = message,
                EndTime = end,
                CurrentStep = "algorithm_failed"
            },
            cancellationToken).ConfigureAwait(false))
        {
            return;
        }
        await taskEvents.AppendAsync(
            new TaskEvent(
                Guid.NewGuid(),
                run.RunId,
                "task.failed",
                "Failed",
                JsonSerializer.Serialize(new { code, message }),
                code,
                message,
                end),
            cancellationToken);
        scheduler.EnqueueWebhook(run.RunId);
    }
}
