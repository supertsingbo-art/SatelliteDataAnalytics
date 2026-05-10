using System.Text.Json;

namespace SatelliteData.Domain.Templates;

/// <summary>
/// 模板生命周期状态。详见 6.2.2 模板生命周期状态机。
/// </summary>
public enum TemplateStatus
{
    /// <summary>草稿。</summary>
    Draft = 0,

    /// <summary>已发布。该版本不可修改，只能克隆新版本。</summary>
    Published = 1,

    /// <summary>已归档。归档版本不出现在任务创建可选列表。</summary>
    Archived = 2
}

/// <summary>
/// 筛选模板版本。<see cref="TemplateId"/> + <see cref="Version"/> 组成主键，<see cref="GroupId"/> 决定可用范围（含后代分组）。
/// </summary>
public sealed record FilterTemplate(
    Guid TemplateId,
    int Version,
    string TemplateName,
    TemplateStatus Status,
    Guid GroupId,
    JsonElement ConfigJson,
    string? Description,
    Guid? CreatedBy,
    DateTimeOffset CreatedAt,
    Guid? UpdatedBy,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt);

/// <summary>
/// 算法模板版本。算法模板全局可见，不绑定卫星分组（详见 6.2.4.1）。
/// </summary>
public sealed record AlgorithmTemplate(
    Guid TemplateId,
    int Version,
    string TemplateName,
    TemplateStatus Status,
    JsonElement ReactFlowJson,
    JsonElement ConfigJson,
    int NodeCount,
    string? Description,
    Guid? CreatedBy,
    DateTimeOffset CreatedAt,
    Guid? UpdatedBy,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt);
