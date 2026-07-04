using System.Text.Json;
using Microsoft.Extensions.Logging;
using SatelliteData.Application.Assets;
using SatelliteData.Application.Tasks;
using SatelliteData.Application.Templates;
using SatelliteData.Domain.Assets;
using SatelliteData.Domain.Tasks;
using static SatelliteData.Application.Tasks.PreprocessTaskLabels;

namespace SatelliteData.Application.Pipeline;

public sealed record PreprocessClaimPlanResult
{
    public bool Succeeded { get; init; }

    public IReadOnlyList<PreprocessParamClaimRequest> Claims { get; init; } = [];

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public static PreprocessClaimPlanResult Ok(IReadOnlyList<PreprocessParamClaimRequest> claims) =>
        new() { Succeeded = true, Claims = claims };

    public static PreprocessClaimPlanResult Fail(string code, string message) =>
        new() { Succeeded = false, ErrorCode = code, ErrorMessage = message };
}

/// <summary>根据 task_run 与筛选模板计算预处理参数占位请求（无副作用，供 Pipeline 与执行前预检复用）。</summary>
public sealed class PreprocessClaimPlanner(
    IFilterTemplateRepository filterTemplates,
    IAssetCacheRepository assetCache,
    MongoConnectionPool mongoPool,
    IFilterRuleEvaluator filterEvaluator,
    IConditionHistoryProvider conditionHistoryProvider,
    ConditionRangeEvaluator conditionRangeEvaluator,
    ILogger<PreprocessClaimPlanner> logger)
{
    private sealed record TargetExecutionPlan(
        TargetParamSpec Target,
        IReadOnlyList<TimeRange> ParamRanges);

    public async Task<PreprocessClaimPlanResult> PlanAsync(TaskRun run, CancellationToken cancellationToken)
    {
        if (run.JobType != TaskJobType.Preprocess)
        {
            return PreprocessClaimPlanResult.Fail("PRE_002", "仅预处理任务支持参数冲突预检");
        }

        if (run.FilterTemplateId is null || run.FilterTemplateVersion is null)
        {
            return PreprocessClaimPlanResult.Fail("PRE_002", "PREPROCESS 任务缺少筛选模板外键");
        }

        if (run.WindowStart is null || run.WindowEnd is null)
        {
            return PreprocessClaimPlanResult.Fail("PRE_002", "任务缺少 window_start / window_end，无法确定数据时间窗");
        }

        var filter = await filterTemplates
            .GetVersionAsync(run.FilterTemplateId.Value, run.FilterTemplateVersion.Value, cancellationToken)
            .ConfigureAwait(false);
        if (filter is null)
        {
            return PreprocessClaimPlanResult.Fail("PIPE_002", "筛选模板版本不存在");
        }

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
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return PreprocessClaimPlanResult.Fail("PRE_002", ex.Message);
        }

        SatelliteCache satellite;
        try
        {
            satellite = await assetCache.GetSatelliteAsync(run.TasookNo, run.SatelliteNo, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("卫星缓存不存在");
            _ = await mongoPool.GetConnectionInfoAsync(run.TasookNo, run.SatelliteNo, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return PreprocessClaimPlanResult.Fail("PRE_003", ex.Message);
        }

        if (satellite.MongoInfo is null)
        {
            return PreprocessClaimPlanResult.Fail("PRE_003", "Mongo 连接信息未同步");
        }

        var mongoUri = satellite.MongoInfo.MongoUri;
        var mongoDb = string.IsNullOrWhiteSpace(satellite.MongoInfo.DbName) ? "test" : satellite.MongoInfo.DbName;

        var parameters = (await assetCache.GetParametersAsync(run.TasookNo, run.SatelliteNo, cancellationToken)
            .ConfigureAwait(false)).ToDictionary(p => p.ParamId, StringComparer.Ordinal);

        var (refTasook, refSatellite) = ResolveReferenceSatellite(filter.ConfigJson, run.TasookNo, run.SatelliteNo);
        if (!string.Equals(refTasook, run.TasookNo, StringComparison.Ordinal)
            || !string.Equals(refSatellite, run.SatelliteNo, StringComparison.Ordinal))
        {
            try
            {
                _ = await assetCache.GetSatelliteAsync(refTasook, refSatellite, cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"参考星缓存不存在：{refTasook}/{refSatellite}");
            }
            catch (Exception ex)
            {
                return PreprocessClaimPlanResult.Fail("PRE_003", ex.Message);
            }
        }

        var refParameters = string.Equals(refTasook, run.TasookNo, StringComparison.Ordinal)
                            && string.Equals(refSatellite, run.SatelliteNo, StringComparison.Ordinal)
            ? parameters
            : (await assetCache.GetParametersAsync(refTasook, refSatellite, cancellationToken)
                .ConfigureAwait(false)).ToDictionary(p => p.ParamId, StringComparer.Ordinal);

        var durationSeconds = filter.ConfigJson.TryGetProperty("durationSeconds", out var durNode)
                              && durNode.TryGetInt32(out var d)
            ? Math.Max(0, d)
            : 0;

        IReadOnlyList<TimeRange> validRanges;
        if (ConditionConfigParser.TryParse(filter.ConfigJson, out var conditionConfig)
            && conditionConfig is not null)
        {
            try
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
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return PreprocessClaimPlanResult.Fail("PRE_004", ex.Message);
            }
        }
        else
        {
            return PreprocessClaimPlanResult.Fail("PRE_004", "筛选模板缺少 conditionConfig，无法计算有效时间段");
        }

        if (validRanges.Count == 0)
        {
            return PreprocessClaimPlanResult.Fail("PRE_004", "conditionConfig 未产生有效时间段");
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

        return PreprocessClaimPlanResult.Ok(BuildClaimRequests(targetPlans));
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
                cancellationToken).ConfigureAwait(false);

            paramRanges = conditionRangeEvaluator.EvaluateParameterRanges(
                conditionConfig,
                window,
                conditionSeries);
        }

        IReadOnlyList<TimeRange> instructionRanges = [new TimeRange(window.Start, window.End)];
        if (conditionConfig.InstructionsEnabled
            && (conditionConfig.StartCommands.Count > 0 || conditionConfig.EndCommands.Count > 0))
        {
            var commands = (await assetCache.GetCommandsAsync(referenceTasookNo, referenceSatelliteNo, cancellationToken)
                .ConfigureAwait(false)).ToDictionary(x => x.CommandId, x => x, StringComparer.Ordinal);
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
                cancellationToken).ConfigureAwait(false);
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
}
