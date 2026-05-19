using SatelliteData.Application.Assets;
using SatelliteData.Application.Templates;
using SatelliteData.Domain.Assets;
using SatelliteData.Domain.Templates;

namespace SatelliteData.Application.Tasks;

/// <summary>
/// 预处理入仓任务创建前校验：卫星须已启用；筛选模板须已发布且对目标星可用。
/// </summary>
public sealed class PreprocessTaskValidator(
    IAssetCacheRepository assetCache,
    IFilterTemplateRepository filterTemplates,
    SatelliteGroupService groupService)
{
    public async Task ValidateAsync(PreprocessCreateCommand command, CancellationToken cancellationToken)
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

        if (command.FilterTemplateId == Guid.Empty || command.FilterTemplateVersion <= 0)
        {
            throw new TaskValidationException(TaskErrorCodes.FilterTemplateRequired, "请选择筛选模板");
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
                $"卫星 {command.TasookNo}/{command.SatelliteNo} 已禁用，不能创建预处理任务");
        }

        var template = await filterTemplates.GetVersionAsync(
            command.FilterTemplateId,
            command.FilterTemplateVersion,
            cancellationToken);
        if (template is null)
        {
            throw new TaskValidationException(
                TaskErrorCodes.FilterTemplateNotFound,
                "筛选模板版本不存在");
        }

        if (template.Status != TemplateStatus.Published)
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
        if (!ancestorGroupIds.Contains(template.GroupId))
        {
            throw new TaskValidationException(
                TaskErrorCodes.FilterTemplateNotApplicable,
                "所选筛选模板对当前卫星不可用（不在其分组祖先链上）");
        }
    }
}
