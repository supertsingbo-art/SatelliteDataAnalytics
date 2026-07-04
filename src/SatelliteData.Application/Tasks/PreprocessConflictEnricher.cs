using SatelliteData.Application.Assets;
using SatelliteData.Application.Templates;
using SatelliteData.Domain.Assets;
using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

/// <summary>将参数占位冲突 enrich 为可读明细（Pipeline 失败与执行前预检复用）。</summary>
public sealed class PreprocessConflictEnricher(
    IAssetCacheRepository assetCache,
    IFilterTemplateRepository filterTemplates,
    ITaskRunRepository taskRuns)
{
    public async Task<IReadOnlyList<PreprocessConflictDetailDto>> EnrichAsync(
        TaskRun run,
        IReadOnlyList<PreprocessParamClaimConflict> conflicts,
        CancellationToken cancellationToken)
    {
        if (conflicts.Count == 0)
        {
            return [];
        }

        var parameters = (await assetCache.GetParametersAsync(run.TasookNo, run.SatelliteNo, cancellationToken)
            .ConfigureAwait(false)).ToDictionary(p => p.ParamId, StringComparer.Ordinal);

        var templateNames = new Dictionary<(Guid Id, int Version), string?>();
        var conflictJobIds = new Dictionary<Guid, string?>();

        var result = new List<PreprocessConflictDetailDto>();
        foreach (var conflict in conflicts
                     .OrderBy(c => c.ParamId, StringComparer.Ordinal)
                     .ThenBy(c => c.ConflictStatus, StringComparer.Ordinal))
        {
            parameters.TryGetValue(conflict.ParamId, out var paramMeta);
            var templateKey = (conflict.ConflictFilterTemplateId, conflict.ConflictFilterTemplateVersion);
            if (!templateNames.TryGetValue(templateKey, out var templateName))
            {
                var template = await filterTemplates
                    .GetVersionAsync(
                        conflict.ConflictFilterTemplateId,
                        conflict.ConflictFilterTemplateVersion,
                        cancellationToken)
                    .ConfigureAwait(false);
                templateName = template?.TemplateName;
                templateNames[templateKey] = templateName;
            }

            if (!conflictJobIds.TryGetValue(conflict.ConflictRunId, out var conflictJobId))
            {
                var conflictRun = await taskRuns
                    .GetByRunIdAsync(conflict.ConflictRunId, cancellationToken)
                    .ConfigureAwait(false);
                conflictJobId = conflictRun?.JobId;
                conflictJobIds[conflict.ConflictRunId] = conflictJobId;
            }

            result.Add(new PreprocessConflictDetailDto(
                conflict.ParamId,
                paramMeta?.DisplayLabel,
                conflict.ConflictStatus,
                conflict.ConflictRunId,
                conflictJobId,
                conflict.ConflictFilterTemplateId,
                conflict.ConflictFilterTemplateVersion,
                templateName));
        }

        return result;
    }

    public static string BuildReadableMessage(IReadOnlyList<PreprocessConflictDetailDto> details)
    {
        if (details.Count == 0)
        {
            return "参数冲突: 未找到可解析的冲突明细";
        }

        var segments = details.Select(d =>
        {
            var paramDisplay = string.IsNullOrWhiteSpace(d.ParamLabel) ? d.ParamId : $"{d.ParamLabel} ({d.ParamId})";
            var statusDisplay = FormatConflictStatus(d.ConflictStatus);
            var templateDisplay = string.IsNullOrWhiteSpace(d.ConflictFilterTemplateName)
                ? $"模板 v{d.ConflictFilterTemplateVersion}"
                : $"{d.ConflictFilterTemplateName} v{d.ConflictFilterTemplateVersion}";
            var jobDisplay = string.IsNullOrWhiteSpace(d.ConflictJobId)
                ? "未知任务"
                : d.ConflictJobId!;
            return $"{paramDisplay} [{statusDisplay}] 与 {templateDisplay}（任务 {jobDisplay}）冲突";
        });

        return $"参数冲突: {string.Join(" | ", segments)}";
    }

    private static string FormatConflictStatus(string status) =>
        string.Equals(status, "ACTIVE", StringComparison.OrdinalIgnoreCase)
            ? "执行中"
            : string.Equals(status, "COMMITTED", StringComparison.OrdinalIgnoreCase)
                ? "已完成"
                : status;
}
