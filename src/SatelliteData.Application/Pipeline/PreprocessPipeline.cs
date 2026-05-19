using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SatelliteData.Application.Assets;
using SatelliteData.Application.Tasks;
using SatelliteData.Application.Templates;
using SatelliteData.Domain.Assets;
using SatelliteData.Domain.Tasks;
using static SatelliteData.Application.Tasks.PreprocessTaskLabels;

namespace SatelliteData.Application.Pipeline;

public sealed class PreprocessPipeline(
    ITaskRunRepository taskRuns,
    ITaskEventRepository taskEvents,
    IFilterTemplateRepository filterTemplates,
    IAssetCacheRepository assetCache,
    MongoConnectionPool mongoPool,
    IFilterRuleEvaluator filterEvaluator,
    IMongoPkgSeriesReader mongoPkgReader,
    IMongoRawSeriesReader mongoRawReader,
    RuleTreeSegmentEvaluator ruleTreeEvaluator,
    IOutlierDetector outlierDetector,
    IClickHouseGateway clickHouse,
    IHqParamMetadataRepository hqMetadata,
    IPreprocessOutlierSegmentRepository outlierSegments,
    PreprocessScheduleService scheduleService,
    IBackgroundJobScheduler scheduler,
    IOptions<PipelineOptions> pipelineOptions,
    ILogger<PreprocessPipeline> logger) : IPreprocessPipeline
{
    public async Task ExecuteAsync(Guid runId, CancellationToken cancellationToken)
    {
        var opt = pipelineOptions.Value;
        var run = await taskRuns.GetByRunIdAsync(runId, cancellationToken)
            ?? throw new InvalidOperationException($"task_run 不存在：{runId}");

        if (run.Status == TaskRunStatus.Cancelled)
        {
            return;
        }

        if (await TaskRunCancellation.IsCancelledAsync(taskRuns, runId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        run = run with
        {
            Status = TaskRunStatus.Running,
            StartTime = run.StartTime ?? DateTimeOffset.UtcNow,
            ProgressPercent = TaskProgressBands.AssetResolveMax,
            CurrentStep = "asset_resolve"
        };
        await taskRuns.UpdateAsync(run, cancellationToken);

        var needsAlgorithm = run.JobType == TaskJobType.Pipeline;
        if (run.FilterTemplateId is null || run.FilterTemplateVersion is null)
        {
            await FailAsync(
                run,
                "PIPE_001",
                needsAlgorithm ? "PIPELINE 任务缺少筛选模板外键" : "PREPROCESS 任务缺少筛选模板外键",
                cancellationToken);
            return;
        }

        if (needsAlgorithm && (run.AlgorithmTemplateId is null || run.AlgorithmTemplateVersion is null))
        {
            await FailAsync(run, "PIPE_001", "PIPELINE 任务缺少算法模板外键", cancellationToken);
            return;
        }

        var filterTemplateId = run.FilterTemplateId!.Value;
        var filterTemplateVersion = run.FilterTemplateVersion!.Value;

        var filter = await filterTemplates.GetVersionAsync(
            filterTemplateId,
            filterTemplateVersion,
            cancellationToken);
        if (filter is null)
        {
            await FailAsync(run, "PIPE_002", "筛选模板版本不存在", cancellationToken);
            return;
        }

        // 数据时间窗仅以 task_run.window_start / window_end 为准（立即/一次定时为用户所选时段，每天定时为前一日同刻至当日同刻）。
        // test_batch_name 仅用于界面展示与 ClickHouse/PG 元数据中的阶段标签，不与 test_batch_cache 按时间重叠匹配。
        if (run.WindowStart is null || run.WindowEnd is null)
        {
            await FailAsync(run, "PRE_002", "任务缺少 window_start / window_end，无法确定数据时间窗", cancellationToken);
            return;
        }

        var warehouseBatchLabel = string.IsNullOrWhiteSpace(run.TestBatchName)
            ? CustomTimeWindowDisplayName
            : run.TestBatchName.Trim();

        EffectiveWindow window;
        IReadOnlyList<TargetParamSpec> targets;
        try
        {
            (window, targets) = await filterEvaluator.EvaluateAsync(
                filter.ConfigJson,
                run.TasookNo,
                run.SatelliteNo,
                testBatchId: null,
                run.WindowStart,
                run.WindowEnd,
                testBatches: Array.Empty<TestBatchCache>(),
                cancellationToken);
        }
        catch (Exception ex)
        {
            await FailAsync(run, "PRE_002", ex.Message, cancellationToken);
            return;
        }

        run = (await taskRuns.GetByRunIdAsync(runId, cancellationToken))!;
        run = run with { ProgressPercent = TaskProgressBands.PreprocessMax, CurrentStep = "preprocess" };
        await taskRuns.UpdateAsync(run, cancellationToken);

        SatelliteCache satellite;
        try
        {
            satellite = await assetCache.GetSatelliteAsync(run.TasookNo, run.SatelliteNo, cancellationToken)
                ?? throw new InvalidOperationException("卫星缓存不存在");
            _ = await mongoPool.GetConnectionInfoAsync(run.TasookNo, run.SatelliteNo, cancellationToken);
        }
        catch (Exception ex)
        {
            await FailAsync(run, "PRE_003", ex.Message, cancellationToken);
            return;
        }

        if (satellite.MongoInfo is null)
        {
            await FailAsync(run, "PRE_003", "Mongo 连接信息未同步", cancellationToken);
            return;
        }

        var mongoUri = satellite.MongoInfo.MongoUri;
        var mongoDb = string.IsNullOrWhiteSpace(satellite.MongoInfo.DbName) ? "test" : satellite.MongoInfo.DbName;

        var parameters = (await assetCache.GetParametersAsync(run.TasookNo, run.SatelliteNo, cancellationToken))
            .ToDictionary(p => p.ParamId, StringComparer.Ordinal);

        var (refTasook, refSatellite) = ResolveReferenceSatellite(filter.ConfigJson, run.TasookNo, run.SatelliteNo);
        var refParameters = string.Equals(refTasook, run.TasookNo, StringComparison.Ordinal)
                            && string.Equals(refSatellite, run.SatelliteNo, StringComparison.Ordinal)
            ? parameters
            : (await assetCache.GetParametersAsync(refTasook, refSatellite, cancellationToken))
                .ToDictionary(p => p.ParamId, StringComparer.Ordinal);

        await clickHouse.EnsureHqParamPointTableAsync(cancellationToken);

        var durationSeconds = filter.ConfigJson.TryGetProperty("durationSeconds", out var durNode)
                              && durNode.TryGetInt32(out var d)
            ? Math.Max(0, d)
            : 0;

        IReadOnlyList<TimeRange> validRanges;
        if (filter.ConfigJson.TryGetProperty("ruleTree", out var ruleTree)
            && RuleTreeSegmentEvaluator.HasConditionParameters(ruleTree))
        {
            var conditionParamIds = RuleTreeSegmentEvaluator.CollectConditionParamIds(ruleTree);
            var conditionSeries = new Dictionary<string, IReadOnlyList<RawSeriesPoint>>(StringComparer.Ordinal);
            foreach (var paramId in conditionParamIds)
            {
                var series = await ReadParamSeriesAsync(
                    mongoUri,
                    mongoDb,
                    refTasook,
                    refSatellite,
                    refParameters,
                    paramId,
                    window.Start,
                    window.End,
                    opt,
                    cancellationToken);
                conditionSeries[paramId] = series;
            }

            validRanges = ruleTreeEvaluator.ComputeValidRanges(
                ruleTree,
                durationSeconds,
                window,
                conditionSeries);
        }
        else
        {
            validRanges = [new TimeRange(window.Start, window.End)];
            logger.LogDebug(
                "目标参数有效窗=任务数据时间范围 {Start:o}..{End:o}（无 ruleTree 参数条件）",
                window.Start,
                window.End);
        }

        if (validRanges.Count == 0)
        {
            await FailAsync(run, "PRE_004", "ruleTree 未产生有效时间段", cancellationToken);
            return;
        }

        ulong versionCounter = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var buffer = new List<string>();
        var allOutlierSegments = new List<PreprocessOutlierSegment>();
        var now = DateTimeOffset.UtcNow;

        foreach (var spec in targets)
        {
            if (await TaskRunCancellation.IsCancelledAsync(taskRuns, runId, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            var paramRanges = RuleTreeSegmentEvaluator.ApplyBuffer(
                validRanges,
                window,
                spec.BoundaryBufferBeforeSec,
                spec.BoundaryBufferAfterSec);

            if (paramRanges.Count == 0)
            {
                logger.LogWarning("参数 {Param} 缓冲后无有效窗，跳过", spec.ParamId);
                continue;
            }

            var points = new List<RawSeriesPoint>();
            foreach (var range in paramRanges)
            {
                var chunk = await ReadParamSeriesAsync(
                    mongoUri,
                    mongoDb,
                    run.TasookNo,
                    run.SatelliteNo,
                    parameters,
                    spec.ParamId,
                    range.Start,
                    range.End,
                    opt,
                    cancellationToken);
                points.AddRange(chunk);
            }

            points = points.OrderBy(p => p.Ts).ToList();

            if (points.Count == 0)
            {
                await FailAsync(run, "PRE_001", $"无有效数据：{spec.ParamId}", cancellationToken);
                return;
            }

            parameters.TryGetValue(spec.ParamId, out var pcache);
            var values = points.Select(p => p.Value).ToList();
            var sigmaK = 3d;
            var flags = outlierDetector.MarkOutliers(
                values,
                spec.OutlierMethod,
                pcache?.ValueMin,
                pcache?.ValueMax,
                sigmaK);

            for (var i = 0; i < points.Count; i++)
            {
                versionCounter++;
                var row = new Dictionary<string, object?>
                {
                    ["tasook_no"] = run.TasookNo,
                    ["satellite_no"] = run.SatelliteNo,
                    ["test_batch_id"] = warehouseBatchLabel,
                    ["param_id"] = spec.ParamId,
                    ["ts"] = points[i].Ts.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                    ["raw_value"] = points[i].Value,
                    ["processed_value"] = points[i].Value,
                    ["is_outlier"] = flags[i],
                    ["version"] = versionCounter
                };
                buffer.Add(JsonSerializer.Serialize(row));
                if (buffer.Count >= opt.ClickHouseBatchSize)
                {
                    await clickHouse.InsertJsonEachRowAsync("hq_param_point", buffer, cancellationToken);
                    buffer.Clear();
                }
            }

            allOutlierSegments.AddRange(
                RuleTreeSegmentEvaluator.MergeOutlierSegments(
                    runId,
                    run.TasookNo,
                    run.SatelliteNo,
                    spec.ParamId,
                    points,
                    flags,
                    spec.OutlierMethod,
                    now));

            await hqMetadata.InsertAsync(
                new HqParamMetadataRow(
                    Guid.NewGuid(),
                    runId,
                    run.TasookNo,
                    run.SatelliteNo,
                    warehouseBatchLabel,
                    spec.ParamId,
                    window.Start,
                    window.End,
                    filterTemplateId,
                    filterTemplateVersion,
                    spec.OutlierMethod,
                    null),
                cancellationToken);
        }

        if (buffer.Count > 0)
        {
            await clickHouse.InsertJsonEachRowAsync("hq_param_point", buffer, cancellationToken);
        }

        if (allOutlierSegments.Count > 0)
        {
            await outlierSegments.InsertBatchAsync(allOutlierSegments, cancellationToken);
        }

        if (await TaskRunCancellation.IsCancelledAsync(taskRuns, runId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        run = (await taskRuns.GetByRunIdAsync(runId, cancellationToken))!;
        if (run.Status == TaskRunStatus.Cancelled)
        {
            return;
        }

        var end = DateTimeOffset.UtcNow;

        if (run.JobType == TaskJobType.Preprocess)
        {
            run = run with
            {
                Status = TaskRunStatus.Succeeded,
                ProgressPercent = 95m,
                CurrentStep = "preprocess_done",
                EndTime = end
            };
            await taskRuns.UpdateAsync(run, cancellationToken);
            await scheduleService.UpdateScheduleFromRunAsync(run, cancellationToken);
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
            scheduler.EnqueueWebhook(runId);
            return;
        }

        run = run with { ProgressPercent = TaskProgressBands.AlgorithmMax - 1m, CurrentStep = "preprocess_done" };
        await taskRuns.UpdateAsync(run, cancellationToken);

        scheduler.EnqueueAlgorithm(runId);
    }

    private async Task<IReadOnlyList<RawSeriesPoint>> ReadParamSeriesAsync(
        string mongoUri,
        string mongoDb,
        string tasookNo,
        string satelliteNo,
        IReadOnlyDictionary<string, ParamCache> parameters,
        string paramId,
        DateTimeOffset start,
        DateTimeOffset end,
        PipelineOptions opt,
        CancellationToken cancellationToken)
    {
        if (!parameters.TryGetValue(paramId, out var meta) || meta.PrmSysId is not int prmSysId)
        {
            logger.LogWarning("参数 {Param} 缺少 prm_sys_id，尝试旧版 raw 集合", paramId);
            return await mongoRawReader.ReadSeriesAsync(
                mongoUri,
                mongoDb,
                opt.MongoRawCollection,
                tasookNo,
                satelliteNo,
                "",
                paramId,
                start,
                end,
                cancellationToken);
        }

        return await mongoPkgReader.ReadSeriesAsync(
            mongoUri,
            mongoDb,
            prmSysId,
            meta.ParaId,
            start,
            end,
            cancellationToken);
    }

    private static (string TasookNo, string SatelliteNo) ResolveReferenceSatellite(
        JsonElement config,
        string defaultTasook,
        string defaultSatellite)
    {
        if (!config.TryGetProperty("scope", out var scope) || scope.ValueKind != JsonValueKind.Object)
        {
            return (defaultTasook, defaultSatellite);
        }

        var t = scope.TryGetProperty("referenceTasookNo", out var tNode) && tNode.ValueKind == JsonValueKind.String
            ? tNode.GetString()
            : null;
        var s = scope.TryGetProperty("referenceSatelliteNo", out var sNode) && sNode.ValueKind == JsonValueKind.String
            ? sNode.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(t) || string.IsNullOrWhiteSpace(s))
        {
            return (defaultTasook, defaultSatellite);
        }

        return (t.Trim(), s.Trim());
    }

    private async Task FailAsync(TaskRun run, string code, string message, CancellationToken cancellationToken)
    {
        if (await TaskRunCancellation.IsCancelledAsync(taskRuns, run.RunId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        logger.LogWarning("Run {RunId} failed {Code}: {Message}", run.RunId, code, message);
        var end = DateTimeOffset.UtcNow;
        var failProgress = run.JobType == TaskJobType.Preprocess
            ? TaskProgressBands.WebhookMax
            : TaskProgressBands.AlgorithmMax;
        var failed = run with
        {
            Status = TaskRunStatus.Failed,
            ProgressPercent = failProgress,
            CurrentStep = "failed",
            EndTime = end,
            ErrorCode = code,
            ErrorMsg = message
        };
        await taskRuns.UpdateAsync(failed, cancellationToken);
        await scheduleService.UpdateScheduleFromRunAsync(failed, cancellationToken);
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
