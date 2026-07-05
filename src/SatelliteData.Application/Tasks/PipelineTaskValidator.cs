using SatelliteData.Application.Assets;
using SatelliteData.Application.Templates;
using SatelliteData.Domain.Templates;

namespace SatelliteData.Application.Tasks;

/// <summary>PIPELINE 任务创建前校验：算法模板必填；筛选模板可选（启用时须已发布且对目标星可用）。</summary>
public sealed class PipelineTaskValidator(
    IAssetCacheRepository assetCache,
    IFilterTemplateRepository filterTemplates,
    IAlgorithmTemplateRepository algorithmTemplates,
    SatelliteGroupService groupService)
{
    public static (Guid? FilterTemplateId, int? FilterTemplateVersion) ResolveFilterTemplate(
        bool? useFilterTemplate,
        Guid? filterTemplateId,
        int? filterTemplateVersion)
    {
        var hasFilterId = filterTemplateId is not null;
        var hasFilterVersion = filterTemplateVersion is not null;
        if (hasFilterId != hasFilterVersion)
        {
            throw new TaskValidationException(
                TaskErrorCodes.ValidationFailed,
                "筛选模板 ID 与版本须同时提供或同时省略");
        }

        var useFilter = useFilterTemplate ?? hasFilterId;
        if (!useFilter)
        {
            if (hasFilterId)
            {
                throw new TaskValidationException(
                    TaskErrorCodes.ValidationFailed,
                    "未启用预处理时不应提供筛选模板");
            }

            return (null, null);
        }

        if (!hasFilterId)
        {
            throw new TaskValidationException(
                TaskErrorCodes.FilterTemplateRequired,
                "启用预处理时必须选择筛选模板");
        }

        return (filterTemplateId, filterTemplateVersion);
    }

    public async Task ValidateAsync(PipelineCreateCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.TasookNo))
        {
            throw new TaskValidationException(TaskErrorCodes.TasookRequired, "请选择型号");
        }

        if (string.IsNullOrWhiteSpace(command.SatelliteNo))
        {
            throw new TaskValidationException(TaskErrorCodes.SatelliteRequired, "请选择卫星");
        }

        if (command.WindowStart is null || command.WindowEnd is null)
        {
            throw new TaskValidationException(
                TaskErrorCodes.WindowRequired,
                "请填写数据时间范围（开始日期与结束日期）");
        }

        if (command.WindowStart.Value >= command.WindowEnd.Value)
        {
            throw new InvalidTaskWindowException();
        }

        if (command.AlgorithmTemplateId == Guid.Empty || command.AlgorithmTemplateVersion <= 0)
        {
            throw new TaskValidationException(
                TaskErrorCodes.AlgorithmTemplateRequired,
                "请选择算法模板");
        }

        var hasFilterId = command.FilterTemplateId is not null;
        var hasFilterVersion = command.FilterTemplateVersion is not null;
        if (hasFilterId != hasFilterVersion)
        {
            throw new TaskValidationException(
                TaskErrorCodes.ValidationFailed,
                "筛选模板 ID 与版本须同时提供或同时省略");
        }

        var satellite = await assetCache.GetSatelliteAsync(command.TasookNo, command.SatelliteNo, cancellationToken);
        if (satellite is null)
        {
            throw new TaskValidationException(
                TaskErrorCodes.SatelliteNotFound,
                $"卫星缓存不存在：{command.TasookNo}/{command.SatelliteNo}");
        }

        if (!satellite.IsEnabled)
        {
            throw new TaskValidationException(
                TaskErrorCodes.SatelliteDisabled,
                $"卫星 {command.TasookNo}/{command.SatelliteNo} 已禁用，不能创建任务");
        }

        var algoTemplate = await algorithmTemplates.GetVersionAsync(
            command.AlgorithmTemplateId,
            command.AlgorithmTemplateVersion,
            cancellationToken);
        if (algoTemplate is null)
        {
            throw new TaskValidationException(
                TaskErrorCodes.AlgorithmTemplateNotFound,
                "算法模板版本不存在");
        }

        if (algoTemplate.Status != TemplateStatus.Published)
        {
            throw new TaskValidationException(
                TaskErrorCodes.AlgorithmTemplateNotPublished,
                "算法模板须为已发布状态");
        }

        if (!hasFilterId)
        {
            return;
        }

        var filterTemplate = await filterTemplates.GetVersionAsync(
            command.FilterTemplateId!.Value,
            command.FilterTemplateVersion!.Value,
            cancellationToken);
        if (filterTemplate is null)
        {
            throw new TaskValidationException(
                TaskErrorCodes.FilterTemplateNotFound,
                "筛选模板版本不存在");
        }

        if (filterTemplate.Status != TemplateStatus.Published)
        {
            throw new TaskValidationException(
                TaskErrorCodes.FilterTemplateNotPublished,
                "筛选模板须为已发布状态");
        }

        var ancestors = await groupService.GetAncestorChainAsync(
            command.TasookNo,
            command.SatelliteNo,
            cancellationToken);
        var ancestorGroupIds = ancestors.Select(a => a.GroupId).ToHashSet();
        if (!ancestorGroupIds.Contains(filterTemplate.GroupId))
        {
            throw new TaskValidationException(
                TaskErrorCodes.FilterTemplateNotApplicable,
                "所选筛选模板对当前卫星不可用（不在其分组祖先链上）");
        }
    }
}
