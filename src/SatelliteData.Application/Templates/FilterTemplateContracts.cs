using System.Text.Json;
using SatelliteData.Domain.Templates;

namespace SatelliteData.Application.Templates;

public sealed record CreateFilterTemplateRequest(
    string TemplateName,
    Guid GroupId,
    JsonElement ConfigJson,
    string? Description);

public sealed record UpdateFilterTemplateRequest(
    string TemplateName,
    Guid GroupId,
    JsonElement ConfigJson,
    string? Description);

public sealed record FilterTemplateView(
    Guid TemplateId,
    int Version,
    string TemplateName,
    TemplateStatus Status,
    Guid GroupId,
    string GroupPath,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt);

public sealed record FilterTemplateDetail(
    FilterTemplateView View,
    JsonElement ConfigJson);

/// <summary>
/// 将筛选模板中「参考卫星」下的 param_id 映射到目标卫星的 param_cache 后的配置快照。
/// </summary>
public sealed record FilterTemplateResolvedDetail(
    JsonElement ConfigJson,
    IReadOnlyList<string> ResolutionWarnings);

public sealed record FilterTemplateListRequest(
    Guid? GroupId,
    TemplateStatus? Status,
    string? Keyword,
    int PageNo = 1,
    int PageSize = 20);

public sealed record FilterTemplateApplicableRequest(string TasookNo, string SatelliteNo);

public interface IFilterTemplateRepository
{
    Task<IReadOnlyCollection<FilterTemplate>> GetAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyCollection<FilterTemplate>> GetByTemplateIdAsync(Guid templateId, CancellationToken cancellationToken);

    Task<FilterTemplate?> GetVersionAsync(Guid templateId, int version, CancellationToken cancellationToken);

    Task<int> GetMaxVersionAsync(Guid templateId, CancellationToken cancellationToken);

    Task SaveAsync(FilterTemplate template, CancellationToken cancellationToken);

    Task DeleteAsync(Guid templateId, int version, CancellationToken cancellationToken);
}
