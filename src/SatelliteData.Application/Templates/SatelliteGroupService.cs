using SatelliteData.Domain.Templates;

namespace SatelliteData.Application.Templates;

public sealed class SatelliteGroupService(
    ISatelliteGroupRepository groupRepository,
    ISatelliteGroupMemberRepository memberRepository)
{
    public async Task<IReadOnlyList<SatelliteGroupNode>> GetTreeAsync(CancellationToken cancellationToken)
    {
        var groups = await groupRepository.GetAllAsync(cancellationToken);
        if (groups.Count == 0)
        {
            return Array.Empty<SatelliteGroupNode>();
        }

        var allMembers = await memberRepository.GetAllAsync(cancellationToken);
        var groupById = groups.ToDictionary(g => g.GroupId);

        var directCounts = allMembers
            .GroupBy(m => m.GroupId)
            .ToDictionary(g => g.Key, g => g.Count());

        var descendantCounts = new Dictionary<Guid, int>();
        foreach (var group in groups)
        {
            var count = allMembers.Count(member =>
            {
                if (!groupById.TryGetValue(member.GroupId, out var memberGroup))
                {
                    return false;
                }
                return memberGroup.GroupPath.StartsWith(group.GroupPath, StringComparison.Ordinal);
            });
            descendantCounts[group.GroupId] = count;
        }

        return BuildTree(groups, directCounts, descendantCounts);
    }

    public async Task<SatelliteGroupNode> GetByIdAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var group = await EnsureExistsAsync(groupId, cancellationToken);
        var directCount = await memberRepository.CountByGroupAsync(groupId, cancellationToken);
        var descendantCount = await CountDescendantMembersAsync(group.GroupPath, cancellationToken);
        return ToNode(group, directCount, descendantCount, Array.Empty<SatelliteGroupNode>());
    }

    public async Task<SatelliteGroupNode> CreateAsync(
        CreateSatelliteGroupRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.GroupName))
        {
            throw new TemplateGovernanceException(
                TemplateErrorCodes.GroupSiblingNameDuplicated,
                "分组名称不能为空");
        }

        SatelliteGroup? parent = null;
        if (request.ParentGroupId.HasValue)
        {
            parent = await EnsureExistsAsync(request.ParentGroupId.Value, cancellationToken);
        }

        if (await groupRepository.SiblingNameExistsAsync(request.ParentGroupId, request.GroupName, null, cancellationToken))
        {
            throw new TemplateGovernanceException(
                TemplateErrorCodes.GroupSiblingNameDuplicated,
                "同一父分组下已存在同名分组");
        }

        var groupId = Guid.NewGuid();
        var path = ResolvePath(parent, request.GroupName);
        var now = DateTimeOffset.UtcNow;
        var group = new SatelliteGroup(
            groupId,
            request.ParentGroupId,
            request.GroupName.Trim(),
            path,
            request.SortOrder,
            request.Description,
            now,
            now);

        await groupRepository.SaveAsync(group, cancellationToken);
        return ToNode(group, 0, 0, Array.Empty<SatelliteGroupNode>());
    }

    public async Task<SatelliteGroupNode> UpdateAsync(
        Guid groupId,
        UpdateSatelliteGroupRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await EnsureExistsAsync(groupId, cancellationToken);

        if (request.ParentGroupId == groupId)
        {
            throw new TemplateGovernanceException(TemplateErrorCodes.GroupCircular, "分组不能以自身作为父分组");
        }

        SatelliteGroup? newParent = null;
        if (request.ParentGroupId.HasValue && request.ParentGroupId != existing.ParentGroupId)
        {
            newParent = await EnsureExistsAsync(request.ParentGroupId.Value, cancellationToken);
            if (newParent.GroupPath.StartsWith(existing.GroupPath, StringComparison.Ordinal))
            {
                throw new TemplateGovernanceException(
                    TemplateErrorCodes.GroupCircular,
                    "目标父分组位于当前分组的子树下，会形成循环");
            }
        }
        else if (request.ParentGroupId == existing.ParentGroupId && existing.ParentGroupId.HasValue)
        {
            newParent = await EnsureExistsAsync(existing.ParentGroupId.Value, cancellationToken);
        }

        if (await groupRepository.SiblingNameExistsAsync(request.ParentGroupId, request.GroupName, groupId, cancellationToken))
        {
            throw new TemplateGovernanceException(
                TemplateErrorCodes.GroupSiblingNameDuplicated,
                "同一父分组下已存在同名分组");
        }

        var newPath = ResolvePath(newParent, request.GroupName);
        var now = DateTimeOffset.UtcNow;

        var updated = existing with
        {
            ParentGroupId = request.ParentGroupId,
            GroupName = request.GroupName.Trim(),
            SortOrder = request.SortOrder,
            Description = request.Description,
            GroupPath = newPath,
            UpdatedAt = now
        };

        await groupRepository.SaveAsync(updated, cancellationToken);

        if (!string.Equals(existing.GroupPath, newPath, StringComparison.Ordinal))
        {
            await RefreshDescendantPathsAsync(existing.GroupPath, newPath, cancellationToken);
        }

        var directCount = await memberRepository.CountByGroupAsync(groupId, cancellationToken);
        var descendantCount = await CountDescendantMembersAsync(newPath, cancellationToken);
        return ToNode(updated, directCount, descendantCount, Array.Empty<SatelliteGroupNode>());
    }

    public async Task DeleteAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var existing = await EnsureExistsAsync(groupId, cancellationToken);

        if (existing.ParentGroupId is null)
        {
            throw new TemplateGovernanceException(TemplateErrorCodes.GroupDeleteRefused, "默认根分组不可删除");
        }

        if (await groupRepository.HasDirectChildrenAsync(groupId, cancellationToken))
        {
            throw new TemplateGovernanceException(TemplateErrorCodes.GroupDeleteRefused, "分组下仍有子分组，删除被拒绝");
        }

        if (await memberRepository.CountByGroupAsync(groupId, cancellationToken) > 0)
        {
            throw new TemplateGovernanceException(TemplateErrorCodes.GroupDeleteRefused, "分组下仍有卫星成员，删除被拒绝");
        }

        await groupRepository.DeleteAsync(groupId, cancellationToken);
    }

    public async Task<IReadOnlyCollection<SatelliteGroupMemberDto>> GetMembersAsync(
        Guid groupId,
        bool includeDescendants,
        CancellationToken cancellationToken)
    {
        var group = await EnsureExistsAsync(groupId, cancellationToken);
        IReadOnlyCollection<SatelliteGroupMember> members;
        if (includeDescendants)
        {
            var allGroups = await groupRepository.GetAllAsync(cancellationToken);
            var inSubtree = allGroups
                .Where(g => g.GroupPath.StartsWith(group.GroupPath, StringComparison.Ordinal))
                .Select(g => g.GroupId)
                .ToHashSet();
            var allMembers = await memberRepository.GetAllAsync(cancellationToken);
            members = allMembers.Where(m => inSubtree.Contains(m.GroupId)).ToArray();
        }
        else
        {
            members = await memberRepository.GetByGroupAsync(groupId, cancellationToken);
        }

        var groupMap = (await groupRepository.GetAllAsync(cancellationToken))
            .ToDictionary(item => item.GroupId);

        return members
            .Select(member => new SatelliteGroupMemberDto(
                member.TasookNo,
                member.SatelliteNo,
                member.GroupId,
                groupMap.TryGetValue(member.GroupId, out var g) ? g.GroupPath : group.GroupPath))
            .ToArray();
    }

    public async Task AddMembersAsync(
        Guid groupId,
        AddGroupMembersRequest request,
        CancellationToken cancellationToken)
    {
        var group = await EnsureExistsAsync(groupId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var sat in request.Satellites)
        {
            await memberRepository.UpsertAsync(
                new SatelliteGroupMember(sat.TasookNo, sat.SatelliteNo, group.GroupId, now),
                cancellationToken);
        }
    }

    public async Task RemoveMemberAsync(
        Guid groupId,
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken)
    {
        await EnsureExistsAsync(groupId, cancellationToken);
        var membership = await memberRepository.GetMembershipAsync(tasookNo, satelliteNo, cancellationToken);
        if (membership is null || membership.GroupId != groupId)
        {
            return;
        }

        await memberRepository.DeleteAsync(tasookNo, satelliteNo, cancellationToken);
    }

    /// <summary>
    /// 解析卫星归属的祖先链（含自身分组），按从根到叶顺序返回。
    /// 卫星未显式归组时返回根分组（系统初始化已 seed）。
    /// </summary>
    public async Task<IReadOnlyList<SatelliteGroup>> GetAncestorChainAsync(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken)
    {
        var membership = await memberRepository.GetMembershipAsync(tasookNo, satelliteNo, cancellationToken);
        Guid? cursor;
        if (membership is null)
        {
            var root = await groupRepository.GetRootAsync(cancellationToken);
            if (root is null)
            {
                return Array.Empty<SatelliteGroup>();
            }
            cursor = root.GroupId;
        }
        else
        {
            cursor = membership.GroupId;
        }

        var chain = new List<SatelliteGroup>();
        while (cursor.HasValue)
        {
            var group = await groupRepository.GetByIdAsync(cursor.Value, cancellationToken);
            if (group is null)
            {
                break;
            }
            chain.Add(group);
            cursor = group.ParentGroupId;
        }

        chain.Reverse();
        return chain;
    }

    private async Task<int> CountDescendantMembersAsync(string groupPath, CancellationToken cancellationToken)
    {
        var allGroups = await groupRepository.GetAllAsync(cancellationToken);
        var subtree = allGroups
            .Where(g => g.GroupPath.StartsWith(groupPath, StringComparison.Ordinal))
            .Select(g => g.GroupId)
            .ToHashSet();
        var allMembers = await memberRepository.GetAllAsync(cancellationToken);
        return allMembers.Count(member => subtree.Contains(member.GroupId));
    }

    private async Task<SatelliteGroup> EnsureExistsAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var group = await groupRepository.GetByIdAsync(groupId, cancellationToken);
        if (group is null)
        {
            throw new TemplateGovernanceException(TemplateErrorCodes.GroupNotFound, "卫星分组不存在");
        }
        return group;
    }

    private async Task RefreshDescendantPathsAsync(string oldPath, string newPath, CancellationToken cancellationToken)
    {
        var allGroups = await groupRepository.GetAllAsync(cancellationToken);
        foreach (var descendant in allGroups.Where(g =>
                     g.GroupPath != oldPath &&
                     g.GroupPath.StartsWith(oldPath, StringComparison.Ordinal)))
        {
            var refreshed = descendant with
            {
                GroupPath = newPath + descendant.GroupPath[oldPath.Length..],
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await groupRepository.SaveAsync(refreshed, cancellationToken);
        }
    }

    private static string ResolvePath(SatelliteGroup? parent, string groupName)
    {
        var safeName = SanitizeNameSegment(groupName);
        if (parent is null)
        {
            return $"/{safeName}/";
        }
        return parent.GroupPath + safeName + "/";
    }

    private static string SanitizeNameSegment(string name)
    {
        return name.Trim().Replace('/', '_').Replace('\\', '_');
    }

    private static IReadOnlyList<SatelliteGroupNode> BuildTree(
        IReadOnlyCollection<SatelliteGroup> groups,
        Dictionary<Guid, int> directCounts,
        Dictionary<Guid, int> descendantCounts)
    {
        var byParent = groups
            .GroupBy(g => g.ParentGroupId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.SortOrder).ThenBy(x => x.GroupName, StringComparer.Ordinal).ToList());

        IReadOnlyList<SatelliteGroupNode> Build(Guid? parentId)
        {
            if (!byParent.TryGetValue(parentId, out var siblings))
            {
                return Array.Empty<SatelliteGroupNode>();
            }
            return siblings
                .Select(group => ToNode(
                    group,
                    directCounts.GetValueOrDefault(group.GroupId, 0),
                    descendantCounts.GetValueOrDefault(group.GroupId, 0),
                    Build(group.GroupId)))
                .ToList();
        }

        return Build(null);
    }

    private static SatelliteGroupNode ToNode(
        SatelliteGroup group,
        int directCount,
        int descendantCount,
        IReadOnlyList<SatelliteGroupNode> children)
    {
        return new SatelliteGroupNode(
            group.GroupId,
            group.ParentGroupId,
            group.GroupName,
            group.GroupPath,
            group.SortOrder,
            group.Description,
            directCount,
            descendantCount,
            group.CreatedAt,
            group.UpdatedAt,
            children);
    }
}
