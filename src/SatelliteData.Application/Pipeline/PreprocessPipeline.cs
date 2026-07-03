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
    IConditionHistoryProvider conditionHistoryProvider,
    ConditionRangeEvaluator conditionRangeEvaluator,
    IOutlierDetector outlierDetector,
    IClickHouseGateway clickHouse,
    IHqParamMetadataRepository hqMetadata,
    IPreprocessParamClaimRepository paramClaims,
    IPreprocessOutlierSegmentRepository outlierSegments,
    IPreprocessOutlierPointReviewRepository outlierReviews,
    IPreprocessValidRangeRepository validRangeRepository,
    ITaskRunConflictOptionStore conflictOptionStore,
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

        try
        {
            await ExecuteCoreAsync(runId, run, opt, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
    }

    private async Task ExecuteCoreAsync(
        Guid runId,
        TaskRun run,
        PipelineOptions opt,
        CancellationToken cancellationToken)
    {
        await TaskRunCancellation.ThrowIfCancelledAsync(taskRuns, runId, cancellationToken).ConfigureAwait(false);

        run = run with
        {
            Status = TaskRunStatus.Running,
            StartTime = run.StartTime ?? DateTimeOffset.UtcNow,
            ProgressPercent = TaskProgressBands.AssetResolveMax,
            CurrentStep = "asset_resolve"
        };
        if (!await taskRuns.UpdateIfNotCancelledAsync(run, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

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
        await TaskRunCancellation.ThrowIfCancelledAsync(taskRuns, runId, cancellationToken).ConfigureAwait(false);
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
        if (!string.Equals(refTasook, run.TasookNo, StringComparison.Ordinal)
            || !string.Equals(refSatellite, run.SatelliteNo, StringComparison.Ordinal))
        {
            _ = await assetCache.GetSatelliteAsync(refTasook, refSatellite, cancellationToken)
                ?? throw new InvalidOperationException($"参考星缓存不存在：{refTasook}/{refSatellite}");
        }

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
        if (ConditionConfigParser.TryParse(filter.ConfigJson, out var conditionConfig)
            && conditionConfig is not null)
        {
            validRanges = await EvaluateByConditionConfigAsync(
                conditionConfig,
                window,
                durationSeconds,
                mongoUri,
                mongoDb,
                refTasook,
                refSatellite,
                refParameters,
                cancellationToken);
        }
        else
        {
            await FailAsync(run, "PRE_004", "筛选模板缺少 conditionConfig，无法计算有效时间段", cancellationToken);
            return;
        }

        try
        {
            await validRangeRepository.DeleteByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await FailAsync(run, "PRE_003", $"清理有效时间段失败：{ex.Message}", cancellationToken);
            return;
        }

        if (validRanges.Count == 0)
        {
            await FailAsync(run, "PRE_004", "conditionConfig 未产生有效时间段", cancellationToken);
            return;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var rows = validRanges
                .Select(x => new PreprocessValidRange(
                    Guid.NewGuid(),
                    runId,
                    run.TasookNo,
                    run.SatelliteNo,
                    x.Start,
                    x.End,
                    now))
                .ToArray();
            await validRangeRepository.InsertBatchAsync(rows, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await FailAsync(run, "PRE_003", $"保存有效时间段失败：{ex.Message}", cancellationToken);
            return;
        }

        var targetPlans = new List<TargetExecutionPlan>();
        foreach (var spec in targets)
        {
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

            targetPlans.Add(new TargetExecutionPlan(spec, paramRanges));
        }

        var conflictOptions = conflictOptionStore.TryGet(runId, out var selectedConflictOptions)
            ? selectedConflictOptions
            : new PreprocessConflictHandlingOptions();

        try
        {
            await paramClaims.DeleteByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await FailAsync(run, "PRE_003", $"清理参数时间段占位失败：{ex.Message}", cancellationToken);
            return;
        }

        var overwriteClaims = new List<PreprocessParamClaimRequest>();
        var hasSkippedConflicts = false;
        var claimPlans = targetPlans.ToList();
        var claimRequests = BuildClaimRequests(claimPlans);
        var claimsAcquired = false;
        var claimsCommitted = false;
        while (claimRequests.Count > 0)
        {
            PreprocessParamClaimAcquireResult acquireResult;
            try
            {
                acquireResult = await paramClaims.TryAcquireAsync(
                    runId,
                    run.TasookNo,
                    run.SatelliteNo,
                    filterTemplateId,
                    filterTemplateVersion,
                    claimRequests,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await FailAsync(run, "PRE_003", $"申请参数时间段占位失败：{ex.Message}", cancellationToken);
                return;
            }

            if (!acquireResult.Acquired)
            {
                var activeConflicts = acquireResult.Conflicts
                    .Where(c => string.Equals(c.ConflictStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                    .Select(c => c.ParamId)
                    .ToHashSet(StringComparer.Ordinal);
                var committedConflicts = acquireResult.Conflicts
                    .Where(c => string.Equals(c.ConflictStatus, "COMMITTED", StringComparison.OrdinalIgnoreCase))
                    .Select(c => c.ParamId)
                    .ToHashSet(StringComparer.Ordinal);

                var skipParams = new HashSet<string>(StringComparer.Ordinal);
                var overwriteParams = new HashSet<string>(StringComparer.Ordinal);

                if (activeConflicts.Count > 0)
                {
                    if (conflictOptions.OnActiveConflict == ActiveConflictHandling.Skip)
                    {
                        skipParams.UnionWith(activeConflicts);
                    }
                    else
                    {
                        await FailAsync(
                            run,
                            "PRE_006",
                            BuildClaimConflictMessage(acquireResult, conflictOptions),
                            cancellationToken);
                        return;
                    }
                }

                if (committedConflicts.Count > 0)
                {
                    if (conflictOptions.OnCommittedConflict == CommittedConflictHandling.Skip)
                    {
                        skipParams.UnionWith(committedConflicts);
                    }
                    else if (conflictOptions.OnCommittedConflict == CommittedConflictHandling.Overwrite)
                    {
                        overwriteParams.UnionWith(committedConflicts);
                    }
                    else
                    {
                        await FailAsync(
                            run,
                            "PRE_006",
                            BuildClaimConflictMessage(acquireResult, conflictOptions),
                            cancellationToken);
                        return;
                    }
                }

                if (skipParams.Count > 0)
                {
                    claimPlans = claimPlans
                        .Where(p => !skipParams.Contains(p.Target.ParamId))
                        .ToList();
                    hasSkippedConflicts = true;
                }

                if (overwriteParams.Count > 0)
                {
                    var overwriteWindowClaims = claimRequests
                        .Where(c => overwriteParams.Contains(c.ParamId))
                        .ToArray();
                    if (overwriteWindowClaims.Length > 0)
                    {
                        await paramClaims.DeleteCommittedOverlapsAsync(
                            runId,
                            run.TasookNo,
                            run.SatelliteNo,
                            overwriteWindowClaims,
                            cancellationToken).ConfigureAwait(false);
                        overwriteClaims.AddRange(overwriteWindowClaims);
                    }
                }

                claimRequests = BuildClaimRequests(claimPlans);
                continue;
            }

            claimsAcquired = true;
            break;
        }

        try
        {
            await outlierReviews.DeleteByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
            await outlierSegments.DeleteByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);

            ulong versionCounter = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var writeVersionFloorExclusive = versionCounter + 1;
            var buffer = new List<string>();
            var allOutlierSegments = new List<PreprocessOutlierSegment>();
            var allOutlierReviews = new List<PreprocessOutlierPointReview>();
            var now = DateTimeOffset.UtcNow;

            foreach (var plan in claimPlans)
            {
                var spec = plan.Target;
                await TaskRunCancellation.ThrowIfCancelledAsync(taskRuns, runId, cancellationToken)
                    .ConfigureAwait(false);

                var points = new List<RawSeriesPoint>();
                foreach (var range in plan.ParamRanges)
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
                    await TaskRunCancellation.ThrowIfCancelledAsync(taskRuns, runId, cancellationToken)
                        .ConfigureAwait(false);
                }

                points = points.OrderBy(p => p.Ts).ToList();

                if (points.Count == 0)
                {
                    await FailAsync(run, "PRE_001", $"无有效数据：{spec.ParamId}", cancellationToken);
                    return;
                }

                var values = points.Select(p => p.Value).ToList();
                var flags = outlierDetector.MarkOutliers(values, spec.Outlier);

                for (var i = 0; i < points.Count; i++)
                {
                    if (i > 0 && i % 1000 == 0)
                    {
                        await TaskRunCancellation.ThrowIfCancelledAsync(taskRuns, runId, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    versionCounter++;
                    var row = new Dictionary<string, object?>
                    {
                        ["tasook_no"] = run.TasookNo,
                        ["satellite_no"] = run.SatelliteNo,
                        ["test_batch_id"] = warehouseBatchLabel,
                        ["param_id"] = spec.ParamId,
                        ["ts"] = points[i].Ts.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
                        ["raw_value"] = points[i].Value,
                        ["processed_value"] = points[i].Value,
                        ["is_outlier"] = flags[i],
                        ["is_confirmed_outlier"] = 0,
                        ["version"] = versionCounter
                    };
                    buffer.Add(JsonSerializer.Serialize(row));
                    if (flags[i] != 0)
                    {
                        allOutlierReviews.Add(new PreprocessOutlierPointReview(
                            Guid.NewGuid(),
                            runId,
                            run.TasookNo,
                            run.SatelliteNo,
                            spec.ParamId,
                            points[i].Ts,
                            points[i].Value,
                            spec.OutlierMethod,
                            OutlierReviewPointStatus.Pending,
                            null,
                            null,
                            null,
                            now));
                    }

                    if (buffer.Count >= opt.ClickHouseBatchSize)
                    {
                        await TaskRunCancellation.ThrowIfCancelledAsync(taskRuns, runId, cancellationToken)
                            .ConfigureAwait(false);
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

                await TaskRunCancellation.ThrowIfCancelledAsync(taskRuns, runId, cancellationToken)
                    .ConfigureAwait(false);
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
                await TaskRunCancellation.ThrowIfCancelledAsync(taskRuns, runId, cancellationToken)
                    .ConfigureAwait(false);
                await clickHouse.InsertJsonEachRowAsync("hq_param_point", buffer, cancellationToken);
            }

            if (allOutlierReviews.Count > 0)
            {
                await TaskRunCancellation.ThrowIfCancelledAsync(taskRuns, runId, cancellationToken)
                    .ConfigureAwait(false);
                await outlierReviews.InsertBatchAsync(allOutlierReviews, cancellationToken).ConfigureAwait(false);
            }

            if (allOutlierSegments.Count > 0)
            {
                await TaskRunCancellation.ThrowIfCancelledAsync(taskRuns, runId, cancellationToken)
                    .ConfigureAwait(false);
                await outlierSegments.InsertBatchAsync(allOutlierSegments, cancellationToken);
            }

            if (claimsAcquired)
            {
                await paramClaims.MarkCommittedByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
                claimsCommitted = true;
            }

            if (overwriteClaims.Count > 0)
            {
                var cleanupClaims = overwriteClaims
                    .GroupBy(
                        c => c.ParamId,
                        StringComparer.Ordinal)
                    .SelectMany(g =>
                    {
                        var merged = ConditionRangeEvaluator.UnionRanges(
                            g.Select(x => new TimeRange(x.SegmentStart, x.SegmentEnd)).ToArray());
                        return merged.Select(m => new PreprocessParamClaimRequest(g.Key, m.Start, m.End));
                    })
                    .ToArray();
                if (cleanupClaims.Length > 0)
                {
                    try
                    {
                        await clickHouse.DeleteByClaimsAsync(
                            run.TasookNo,
                            run.SatelliteNo,
                            warehouseBatchLabel,
                            cleanupClaims,
                            writeVersionFloorExclusive,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "提交覆盖清理 mutation 失败，run={RunId}", runId);
                    }
                }
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
                var autoCount = allOutlierReviews.Count;
                run = run with
                {
                    Status = TaskRunStatus.Succeeded,
                    ProgressPercent = 95m,
                    EndTime = end,
                    OutlierReviewStatus = autoCount == 0
                        ? OutlierReviewRunStatus.NotRequired
                        : OutlierReviewRunStatus.Pending,
                    OutlierAutoCount = autoCount,
                    OutlierPendingCount = autoCount,
                    OutlierConfirmedCount = 0,
                    OutlierJitterCount = 0,
                    CurrentStep = hasSkippedConflicts
                        ? "preprocess_done(skipped_conflicts)"
                        : "preprocess_done"
                };
                if (!await taskRuns.UpdateIfNotCancelledAsync(run, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
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
            if (!await taskRuns.UpdateIfNotCancelledAsync(run, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            scheduler.EnqueueAlgorithm(runId);
        }
        finally
        {
            conflictOptionStore.Clear(runId);
            if (claimsAcquired && !claimsCommitted)
            {
                try
                {
                    await paramClaims.ReleaseActiveByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "释放预处理参数时间段占位失败，run={RunId}", runId);
                }
            }
        }
    }

    private static IReadOnlyList<PreprocessParamClaimRequest> BuildClaimRequests(
        IReadOnlyList<TargetExecutionPlan> targetPlans)
    {
        var result = new List<PreprocessParamClaimRequest>();
        foreach (var grouped in targetPlans.GroupBy(x => x.Target.ParamId, StringComparer.Ordinal))
        {
            var merged = ConditionRangeEvaluator.UnionRanges(grouped.SelectMany(x => x.ParamRanges).ToArray());
            foreach (var range in merged)
            {
                result.Add(new PreprocessParamClaimRequest(grouped.Key, range.Start, range.End));
            }
        }

        return result;
    }

    private static string BuildClaimConflictMessage(
        PreprocessParamClaimAcquireResult result,
        PreprocessConflictHandlingOptions options)
    {
        if (result.Conflicts.Count == 0)
        {
            return "参数冲突: 未找到可解析的冲突明细";
        }

        var conflicts = result.Conflicts
            .OrderBy(c => c.ParamId, StringComparer.Ordinal)
            .ThenBy(c => c.ConflictStatus, StringComparer.Ordinal)
            .Select(c =>
                $"param_id={c.ParamId},status={c.ConflictStatus},冲突模板={c.ConflictFilterTemplateId}/v{c.ConflictFilterTemplateVersion},冲突任务={c.ConflictRunId}")
            .ToArray();
        return $"参数冲突: {string.Join(" | ", conflicts)}。当前策略(active={options.OnActiveConflict}, committed={options.OnCommittedConflict})";
    }

    private sealed record TargetExecutionPlan(
        TargetParamSpec Target,
        IReadOnlyList<TimeRange> ParamRanges);

    private async Task<IReadOnlyList<TimeRange>> EvaluateByConditionConfigAsync(
        FilterConditionConfig conditionConfig,
        EffectiveWindow window,
        int durationSeconds,
        string mongoUri,
        string mongoDb,
        string referenceTasookNo,
        string referenceSatelliteNo,
        IReadOnlyDictionary<string, ParamCache> referenceParameters,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TimeRange> paramRanges = [new TimeRange(window.Start, window.End)];
        if (conditionConfig.ParametersEnabled && conditionConfig.Parameters.Count > 0)
        {
            var lookups = new List<ParameterHistoryLookup>();
            foreach (var parameter in conditionConfig.Parameters)
            {
                if (!referenceParameters.TryGetValue(parameter.ParamId, out var meta))
                {
                    throw new InvalidOperationException(
                        $"conditionConfig 引用参数不存在于参考星缓存：{referenceTasookNo}/{referenceSatelliteNo} paramId={parameter.ParamId}");
                }

                if (meta.PrmSysId is not int prmSysId)
                {
                    throw new InvalidOperationException($"参数 {parameter.ParamId} 缺少 prm_sys_id，无法查询历史时序");
                }

                lookups.Add(new ParameterHistoryLookup(
                    parameter.ParamId,
                    meta.ParaId,
                    prmSysId));
            }

            var conditionSeries = await conditionHistoryProvider.QueryParameterSeriesAsync(
                mongoUri,
                mongoDb,
                window.Start,
                window.End,
                lookups,
                cancellationToken);

            paramRanges = conditionRangeEvaluator.EvaluateParameterRanges(
                conditionConfig,
                window,
                conditionSeries);
        }

        IReadOnlyList<TimeRange> instructionRanges = [new TimeRange(window.Start, window.End)];
        if (conditionConfig.InstructionsEnabled
            && (conditionConfig.StartCommands.Count > 0 || conditionConfig.EndCommands.Count > 0))
        {
            var commands = (await assetCache.GetCommandsAsync(referenceTasookNo, referenceSatelliteNo, cancellationToken))
                .ToDictionary(x => x.CommandId, x => x, StringComparer.Ordinal);
            var commandLookups = new List<InstructionHistoryLookup>();
            foreach (var instruction in conditionConfig.StartCommands.Concat(conditionConfig.EndCommands))
            {
                if (!int.TryParse(instruction.CommandId, out var cmdId))
                {
                    continue;
                }

                var channelId = instruction.ChannelId;
                if (channelId <= 0
                    && commands.TryGetValue(instruction.CommandId, out var commandMeta)
                    && commandMeta.CmdSysId is int cmdSysId)
                {
                    channelId = cmdSysId;
                }

                commandLookups.Add(new InstructionHistoryLookup(
                    instruction.CommandId,
                    cmdId,
                    Math.Max(0, channelId)));
            }

            commandLookups = commandLookups
                .GroupBy(x => x.CommandId, StringComparer.Ordinal)
                .Select(x => x.First())
                .ToList();
            var history = await conditionHistoryProvider.QueryInstructionHistoryAsync(
                mongoUri,
                mongoDb,
                window.Start,
                window.End,
                commandLookups,
                cancellationToken);
            instructionRanges = conditionRangeEvaluator.EvaluateInstructionRanges(
                conditionConfig,
                window,
                history);
        }

        var ranges = ConditionRangeEvaluator.IntersectRanges(paramRanges, instructionRanges);
        ranges = ConditionRangeEvaluator.ClipToWindow(ranges, window);
        if (durationSeconds > 0)
        {
            var minSpan = TimeSpan.FromSeconds(durationSeconds);
            ranges = ranges
                .Where(x => x.End - x.Start >= minSpan)
                .ToArray();
        }

        return ranges;
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
        if (!await taskRuns.UpdateIfNotCancelledAsync(failed, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

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
