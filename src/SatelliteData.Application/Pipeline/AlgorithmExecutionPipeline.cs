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

        run = run with { CurrentStep = "algorithm", ProgressPercent = TaskProgressBands.AlgorithmMax - 15m };
        await taskRuns.UpdateAsync(run, cancellationToken);

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
            if (await TaskRunCancellation.IsCancelledAsync(taskRuns, runId, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

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

            if (o.Scalar.HasValue)
            {
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
                    ["metric_name"] = "scalar",
                    ["metric_value"] = o.Scalar.Value,
                    ["detail_json"] = "{}"
                }));
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
        await taskRuns.UpdateAsync(run, cancellationToken);
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
        await taskRuns.UpdateAsync(
            run with
            {
                Status = TaskRunStatus.Failed,
                ErrorCode = code,
                ErrorMsg = message,
                EndTime = end,
                CurrentStep = "algorithm_failed"
            },
            cancellationToken);
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
