namespace SatelliteData.Domain.Templates;

/// <summary>
/// 卫星分组节点。<see cref="GroupPath"/> 为物化路径，形如 <c>/root/platform-A/A_remote/</c>，
/// 用于按前缀匹配快速判断祖先链 / 后代链；根分组的 <see cref="ParentGroupId"/> 为 <c>null</c>。
/// </summary>
public sealed record SatelliteGroup(
    Guid GroupId,
    Guid? ParentGroupId,
    string GroupName,
    string GroupPath,
    int SortOrder,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// 卫星归属。一颗卫星仅归属一个分组（PK 为 (tasookNo, satelliteNo)）。
/// </summary>
public sealed record SatelliteGroupMember(
    string TasookNo,
    string SatelliteNo,
    Guid GroupId,
    DateTimeOffset CreatedAt);

public static class SatelliteGroupConstants
{
    public const string DefaultRootName = "默认根分组";
    public const string DefaultRootPath = "/root/";
}
