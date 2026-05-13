using SatelliteData.Application.Templates;
using SatelliteData.Domain.Templates;

namespace SatelliteData.Infrastructure.PostgreSql;

public sealed class InMemorySatelliteGroupRepository : ISatelliteGroupRepository
{
    private readonly Dictionary<Guid, SatelliteGroup> _groups = [];
    private readonly object _gate = new();

    public InMemorySatelliteGroupRepository()
    {
      SeedRootGroup();  
    }

    private void SeedRootGroup()
    {
        var rootId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var root = new SatelliteGroup(
            rootId,
            ParentGroupId: Guid.Empty,// fqb Guid.Parse("98ab5a97-75cb-47ea-9f62-7989fda87ee5"),
            GroupName: SatelliteGroupConstants.DefaultRootName,
            GroupPath: SatelliteGroupConstants.DefaultRootPath,
            SortOrder: 0,
            Description: "系统初始化时创建的默认根分组；未显式归组的卫星挂在此分组下",
            CreatedAt: now,
            UpdatedAt: now);
        _groups[rootId] = root;
    }

    public Task<IReadOnlyCollection<SatelliteGroup>> GetAllAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyCollection<SatelliteGroup>>(_groups.Values.ToArray());
        }
    }

    public Task<SatelliteGroup?> GetByIdAsync(Guid groupId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _groups.TryGetValue(groupId, out var group);
            return Task.FromResult(group);
        }
    }

    public Task<SatelliteGroup?> GetRootAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var root = _groups.Values.SingleOrDefault(item => item.ParentGroupId is null);
            return Task.FromResult(root);
        }
    }

    public Task<IReadOnlyCollection<SatelliteGroup>> GetChildrenAsync(Guid? parentGroupId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var children = _groups.Values
                .Where(item => item.ParentGroupId == parentGroupId)
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.GroupName, StringComparer.Ordinal)
                .ToArray();
            return Task.FromResult<IReadOnlyCollection<SatelliteGroup>>(children);
        }
    }

    public Task SaveAsync(SatelliteGroup group, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _groups[group.GroupId] = group;
            return Task.CompletedTask;
        }
    }

    public Task DeleteAsync(Guid groupId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _groups.Remove(groupId);
            return Task.CompletedTask;
        }
    }

    public Task<bool> HasDirectChildrenAsync(Guid groupId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_groups.Values.Any(item => item.ParentGroupId == groupId));
        }
    }

    public Task<bool> SiblingNameExistsAsync(
        Guid? parentGroupId,
        string groupName,
        Guid? excludeGroupId,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var hit = _groups.Values.Any(item =>
                item.ParentGroupId == parentGroupId
                && item.GroupId != excludeGroupId
                && string.Equals(item.GroupName.Trim(), groupName.Trim(), StringComparison.Ordinal));
            return Task.FromResult(hit);
        }
    }
}

public sealed class InMemorySatelliteGroupMemberRepository : ISatelliteGroupMemberRepository
{
    private readonly Dictionary<(string TasookNo, string SatelliteNo), SatelliteGroupMember> _memberships = [];
    private readonly object _gate = new();

    public Task<IReadOnlyCollection<SatelliteGroupMember>> GetAllAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyCollection<SatelliteGroupMember>>(_memberships.Values.ToArray());
        }
    }

    public Task<IReadOnlyCollection<SatelliteGroupMember>> GetByGroupAsync(Guid groupId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var members = _memberships.Values
                .Where(item => item.GroupId == groupId)
                .ToArray();
            return Task.FromResult<IReadOnlyCollection<SatelliteGroupMember>>(members);
        }
    }

    public Task<SatelliteGroupMember?> GetMembershipAsync(string tasookNo, string satelliteNo, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _memberships.TryGetValue((tasookNo, satelliteNo), out var membership);
            return Task.FromResult(membership);
        }
    }

    public Task UpsertAsync(SatelliteGroupMember member, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _memberships[(member.TasookNo, member.SatelliteNo)] = member;
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string tasookNo, string satelliteNo, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _memberships.Remove((tasookNo, satelliteNo));
        }
        return Task.CompletedTask;
    }

    public Task<int> CountByGroupAsync(Guid groupId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_memberships.Values.Count(item => item.GroupId == groupId));
        }
    }
}
