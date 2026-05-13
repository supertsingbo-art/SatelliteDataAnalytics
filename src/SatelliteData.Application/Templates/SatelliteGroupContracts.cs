using SatelliteData.Domain.Templates;

namespace SatelliteData.Application.Templates;

public sealed record CreateSatelliteGroupRequest(
    Guid? ParentGroupId,
    string GroupName,
    int SortOrder = 0,
    string? Description = null);

public sealed record UpdateSatelliteGroupRequest(
    Guid? ParentGroupId,
    string GroupName,
    int SortOrder,
    string? Description);

public sealed record SatelliteGroupNode(
    Guid GroupId,
    Guid? ParentGroupId,
    string GroupName,
    string GroupPath,
    int SortOrder,
    string? Description,
    int DirectMemberCount,
    int DescendantMemberCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<SatelliteGroupNode> Children);


public sealed record SatelliteGroupMemberDto(
    string TasookNo,
    string SatelliteNo,
    Guid GroupId,
    string GroupPath);

public sealed record AddGroupMembersRequest(
    IReadOnlyList<SatelliteRef> Satellites);

public sealed record SatelliteRef(string TasookNo, string SatelliteNo);

public interface ISatelliteGroupRepository
{
    Task<IReadOnlyCollection<SatelliteGroup>> GetAllAsync(CancellationToken cancellationToken);

    Task<SatelliteGroup?> GetByIdAsync(Guid groupId, CancellationToken cancellationToken);

    Task<SatelliteGroup?> GetRootAsync(CancellationToken cancellationToken);

    Task<IReadOnlyCollection<SatelliteGroup>> GetChildrenAsync(Guid? parentGroupId, CancellationToken cancellationToken);

    Task SaveAsync(SatelliteGroup group, CancellationToken cancellationToken);

    Task DeleteAsync(Guid groupId, CancellationToken cancellationToken);

    Task<bool> HasDirectChildrenAsync(Guid groupId, CancellationToken cancellationToken);

    Task<bool> SiblingNameExistsAsync(Guid? parentGroupId, string groupName, Guid? excludeGroupId, CancellationToken cancellationToken);
}

public interface ISatelliteGroupMemberRepository
{
    Task<IReadOnlyCollection<SatelliteGroupMember>> GetAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyCollection<SatelliteGroupMember>> GetByGroupAsync(Guid groupId, CancellationToken cancellationToken);

    Task<SatelliteGroupMember?> GetMembershipAsync(string tasookNo, string satelliteNo, CancellationToken cancellationToken);

    Task UpsertAsync(SatelliteGroupMember member, CancellationToken cancellationToken);

    Task DeleteAsync(string tasookNo, string satelliteNo, CancellationToken cancellationToken);

    Task<int> CountByGroupAsync(Guid groupId, CancellationToken cancellationToken);
}
