using System.Text.Json;
using System.Text.Json.Nodes;
using SatelliteData.Application.Assets;
using SatelliteData.Domain.Assets;
using SatelliteData.Domain.Templates;

namespace SatelliteData.Application.Templates;

public sealed class FilterTemplateService(
    IFilterTemplateRepository templateRepository,
    ISatelliteGroupRepository groupRepository,
    SatelliteGroupService groupService,
    IAssetCacheRepository assetCacheRepository)
{
    public async Task<PagedResult<FilterTemplateView>> ListAsync(
        FilterTemplateListRequest request,
        CancellationToken cancellationToken)
    {
        var pageNo = request.PageNo <= 0 ? 1 : request.PageNo;
        var pageSize = request.PageSize <= 0 ? 20 : Math.Min(request.PageSize, 100);

        var all = await templateRepository.GetAllAsync(cancellationToken);

        // 仅保留每个 templateId 的「当前展示版本」：发布优先，否则取最大版本号
        var byTemplate = all.GroupBy(t => t.TemplateId);
        var representatives = new List<FilterTemplate>();
        foreach (var grp in byTemplate)
        {
            var latestPublished = grp.Where(t => t.Status == TemplateStatus.Published)
                .OrderByDescending(t => t.Version)
                .FirstOrDefault();
            representatives.Add(latestPublished ?? grp.OrderByDescending(t => t.Version).First());
        }

        if (request.GroupId.HasValue)
        {
            representatives = representatives.Where(t => t.GroupId == request.GroupId.Value).ToList();
        }
        if (request.Status.HasValue)
        {
            representatives = representatives.Where(t => t.Status == request.Status.Value).ToList();
        }
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword!;
            representatives = representatives
                .Where(t => t.TemplateName.Contains(kw, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var ordered = representatives
            .OrderByDescending(t => t.UpdatedAt)
            .ToArray();

        var paged = ordered
            .Skip((pageNo - 1) * pageSize)
            .Take(pageSize)
            .ToArray();

        var groupMap = (await groupRepository.GetAllAsync(cancellationToken)).ToDictionary(g => g.GroupId);
        var items = paged.Select(t => ToView(t, groupMap)).ToArray();

        return new PagedResult<FilterTemplateView>(pageNo, pageSize, ordered.Length, items);
    }

    public async Task<IReadOnlyCollection<FilterTemplateView>> GetVersionsAsync(
        Guid templateId,
        CancellationToken cancellationToken)
    {
        var versions = await templateRepository.GetByTemplateIdAsync(templateId, cancellationToken);
        if (versions.Count == 0)
        {
            throw new TemplateGovernanceException(TemplateErrorCodes.FilterTemplateNotFound, "筛选模板不存在");
        }

        var groupMap = (await groupRepository.GetAllAsync(cancellationToken)).ToDictionary(g => g.GroupId);
        return versions
            .OrderByDescending(t => t.Version)
            .Select(t => ToView(t, groupMap))
            .ToArray();
    }

    public async Task<FilterTemplateDetail> GetVersionDetailAsync(
        Guid templateId,
        int version,
        CancellationToken cancellationToken)
    {
        var template = await templateRepository.GetVersionAsync(templateId, version, cancellationToken)
            ?? throw new TemplateGovernanceException(TemplateErrorCodes.FilterTemplateNotFound, "筛选模板版本不存在");

        var groupMap = (await groupRepository.GetAllAsync(cancellationToken)).ToDictionary(g => g.GroupId);
        return new FilterTemplateDetail(ToView(template, groupMap), template.ConfigJson);
    }

    public async Task<FilterTemplateDetail> CreateAsync(
        CreateFilterTemplateRequest request,
        Guid? operatorId,
        CancellationToken cancellationToken)
    {
        await EnsureGroupExistsAsync(request.GroupId, cancellationToken);
        var configJson = NormalizeConfigJson(request.ConfigJson, request.GroupId,
            await groupRepository.GetByIdAsync(request.GroupId, cancellationToken));
        FilterTemplateValidator.Validate(configJson);
        await EnsureReferenceSatelliteInGroupAsync(request.GroupId, configJson, cancellationToken);
        configJson = await NormalizeReferenceParamsAsync(configJson, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var template = new FilterTemplate(
            TemplateId: Guid.NewGuid(),
            Version: 1,
            TemplateName: request.TemplateName.Trim(),
            Status: TemplateStatus.Draft,
            GroupId: request.GroupId,
            ConfigJson: configJson,
            Description: request.Description,
            CreatedBy: operatorId,
            CreatedAt: now,
            UpdatedBy: operatorId,
            UpdatedAt: now,
            PublishedAt: null);

        await templateRepository.SaveAsync(template, cancellationToken);
        var groupMap = (await groupRepository.GetAllAsync(cancellationToken)).ToDictionary(g => g.GroupId);
        return new FilterTemplateDetail(ToView(template, groupMap), template.ConfigJson);
    }

    public async Task<FilterTemplateDetail> UpdateAsync(
        Guid templateId,
        int version,
        UpdateFilterTemplateRequest request,
        Guid? operatorId,
        CancellationToken cancellationToken)
    {
        var existing = await templateRepository.GetVersionAsync(templateId, version, cancellationToken)
            ?? throw new TemplateGovernanceException(TemplateErrorCodes.FilterTemplateNotFound, "筛选模板版本不存在");

        if (existing.Status != TemplateStatus.Draft)
        {
            throw new TemplateGovernanceException(
                TemplateErrorCodes.FilterTemplateNotEditable,
                "仅 Draft 状态的版本可以直接编辑；请先克隆为新版本");
        }

        await EnsureGroupExistsAsync(request.GroupId, cancellationToken);
        var configJson = NormalizeConfigJson(request.ConfigJson, request.GroupId,
            await groupRepository.GetByIdAsync(request.GroupId, cancellationToken));
        FilterTemplateValidator.Validate(configJson);
        await EnsureReferenceSatelliteInGroupAsync(request.GroupId, configJson, cancellationToken);
        configJson = await NormalizeReferenceParamsAsync(configJson, cancellationToken);

        var updated = existing with
        {
            TemplateName = request.TemplateName.Trim(),
            GroupId = request.GroupId,
            ConfigJson = configJson,
            Description = request.Description,
            UpdatedBy = operatorId,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await templateRepository.SaveAsync(updated, cancellationToken);
        var groupMap = (await groupRepository.GetAllAsync(cancellationToken)).ToDictionary(g => g.GroupId);
        return new FilterTemplateDetail(ToView(updated, groupMap), updated.ConfigJson);
    }

    public async Task<FilterTemplateView> PublishAsync(
        Guid templateId,
        int version,
        Guid? operatorId,
        CancellationToken cancellationToken)
    {
        var existing = await templateRepository.GetVersionAsync(templateId, version, cancellationToken)
            ?? throw new TemplateGovernanceException(TemplateErrorCodes.FilterTemplateNotFound, "筛选模板版本不存在");

        if (existing.Status != TemplateStatus.Draft)
        {
            throw new TemplateGovernanceException(
                TemplateErrorCodes.FilterTemplateInvalidState,
                "只有 Draft 状态可以发布");
        }

        var configJson = await NormalizeReferenceParamsAsync(existing.ConfigJson, cancellationToken);
        FilterTemplateValidator.Validate(configJson);

        var now = DateTimeOffset.UtcNow;
        var updated = existing with
        {
            ConfigJson = configJson,
            Status = TemplateStatus.Published,
            PublishedAt = now,
            UpdatedBy = operatorId,
            UpdatedAt = now
        };
        await templateRepository.SaveAsync(updated, cancellationToken);

        var groupMap = (await groupRepository.GetAllAsync(cancellationToken)).ToDictionary(g => g.GroupId);
        return ToView(updated, groupMap);
    }

    public async Task<FilterTemplateView> ArchiveAsync(
        Guid templateId,
        int version,
        Guid? operatorId,
        CancellationToken cancellationToken)
    {
        var existing = await templateRepository.GetVersionAsync(templateId, version, cancellationToken)
            ?? throw new TemplateGovernanceException(TemplateErrorCodes.FilterTemplateNotFound, "筛选模板版本不存在");

        if (existing.Status == TemplateStatus.Archived)
        {
            throw new TemplateGovernanceException(
                TemplateErrorCodes.FilterTemplateInvalidState,
                "已归档版本不能再次归档");
        }

        var updated = existing with
        {
            Status = TemplateStatus.Archived,
            UpdatedBy = operatorId,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await templateRepository.SaveAsync(updated, cancellationToken);

        var groupMap = (await groupRepository.GetAllAsync(cancellationToken)).ToDictionary(g => g.GroupId);
        return ToView(updated, groupMap);
    }

    public async Task<FilterTemplateDetail> CloneAsync(
        Guid templateId,
        int? sourceVersion,
        Guid? operatorId,
        CancellationToken cancellationToken)
    {
        FilterTemplate source;
        if (sourceVersion.HasValue)
        {
            source = await templateRepository.GetVersionAsync(templateId, sourceVersion.Value, cancellationToken)
                ?? throw new TemplateGovernanceException(TemplateErrorCodes.FilterTemplateNotFound, "源版本不存在");
        }
        else
        {
            var versions = await templateRepository.GetByTemplateIdAsync(templateId, cancellationToken);
            source = versions.OrderByDescending(t => t.Version).FirstOrDefault()
                ?? throw new TemplateGovernanceException(TemplateErrorCodes.FilterTemplateNotFound, "模板无可克隆版本");
        }

        var maxVersion = await templateRepository.GetMaxVersionAsync(templateId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var clone = source with
        {
            Version = maxVersion + 1,
            Status = TemplateStatus.Draft,
            CreatedBy = operatorId,
            CreatedAt = now,
            UpdatedBy = operatorId,
            UpdatedAt = now,
            PublishedAt = null
        };
        await templateRepository.SaveAsync(clone, cancellationToken);

        var groupMap = (await groupRepository.GetAllAsync(cancellationToken)).ToDictionary(g => g.GroupId);
        return new FilterTemplateDetail(ToView(clone, groupMap), clone.ConfigJson);
    }

    public async Task DeleteAsync(
        Guid templateId,
        int version,
        CancellationToken cancellationToken)
    {
        var existing = await templateRepository.GetVersionAsync(templateId, version, cancellationToken)
            ?? throw new TemplateGovernanceException(TemplateErrorCodes.FilterTemplateNotFound, "筛选模板版本不存在");

        if (existing.Status != TemplateStatus.Draft)
        {
            throw new TemplateGovernanceException(
                TemplateErrorCodes.FilterTemplateInvalidState,
                "仅 Draft 状态版本可删除；已发布版本只能归档");
        }

        await templateRepository.DeleteAsync(templateId, version, cancellationToken);
    }

    /// <summary>
    /// 给定卫星 (taskNo, satNo)，返回所有可用的已发布筛选模板（祖先链上的全部分组）。
    /// </summary>
    public async Task<IReadOnlyCollection<FilterTemplateView>> GetApplicableAsync(
        FilterTemplateApplicableRequest request,
        CancellationToken cancellationToken)
    {
        var ancestors = await groupService.GetAncestorChainAsync(request.TasookNo, request.SatelliteNo, cancellationToken);
        var ancestorIds = ancestors.Select(a => a.GroupId).ToHashSet();

        var allTemplates = await templateRepository.GetAllAsync(cancellationToken);
        var groupMap = (await groupRepository.GetAllAsync(cancellationToken)).ToDictionary(g => g.GroupId);

        var grouped = allTemplates
            .Where(t => t.Status == TemplateStatus.Published && ancestorIds.Contains(t.GroupId))
            .GroupBy(t => t.TemplateId)
            .Select(grp => grp.OrderByDescending(t => t.Version).First())
            .OrderBy(t => t.TemplateName, StringComparer.Ordinal);

        return grouped.Select(t => ToView(t, groupMap)).ToArray();
    }

    /// <summary>
    /// 将模板中参考卫星的 param_id 映射到目标卫星（同组内按名称 / 描述语义匹配），供单星消费组级模板时使用。
    /// </summary>
    public async Task<FilterTemplateResolvedDetail> ResolveForSatelliteAsync(
        Guid templateId,
        int version,
        string targetTasookNo,
        string targetSatelliteNo,
        CancellationToken cancellationToken)
    {
        var template = await templateRepository.GetVersionAsync(templateId, version, cancellationToken)
            ?? throw new TemplateGovernanceException(TemplateErrorCodes.FilterTemplateNotFound, "筛选模板版本不存在");

        if (!await groupService.IsSatelliteInGroupSubtreeAsync(
                template.GroupId,
                targetTasookNo,
                targetSatelliteNo,
                cancellationToken))
        {
            throw new TemplateGovernanceException(
                TemplateErrorCodes.FilterTemplateResolveFailed,
                "目标卫星不在该模板归属分组（含子分组）的成员范围内，无法解析");
        }

        var config = template.ConfigJson;
        var (refTasook, refSat) = ReadScopeReference(config);
        if (string.Equals(refTasook, targetTasookNo, StringComparison.Ordinal)
            && string.Equals(refSat, targetSatelliteNo, StringComparison.Ordinal))
        {
            return new FilterTemplateResolvedDetail(config, Array.Empty<string>());
        }

        var refParams = (await assetCacheRepository.GetParametersAsync(refTasook, refSat, cancellationToken)).ToArray();
        var targetParams = (await assetCacheRepository.GetParametersAsync(targetTasookNo, targetSatelliteNo, cancellationToken))
            .ToArray();
        var refById = refParams.ToDictionary(p => p.ParamId, StringComparer.Ordinal);

        var warnings = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        FilterTemplateConfigMapper.CollectParamIds(config, ids);

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            var mapped = FilterTemplateConfigMapper.MapParamId(id, refById, targetParams, warnings);
            if (mapped is null)
            {
                throw new TemplateGovernanceException(
                    TemplateErrorCodes.FilterTemplateResolveFailed,
                    $"参数映射失败：{warnings[^1]}");
            }

            map[id] = mapped;
        }

        var remapped = FilterTemplateConfigMapper.ApplyParamIdMap(config, map);
        PatchTargetParamNames(remapped, targetParams, out var finalConfig);
        return new FilterTemplateResolvedDetail(finalConfig, warnings);
    }

    private static void PatchTargetParamNames(
        JsonElement remapped,
        IReadOnlyList<ParamCache> targetParams,
        out JsonElement result)
    {
        var targetById = targetParams.ToDictionary(p => p.ParamId, StringComparer.Ordinal);
        var node = JsonNode.Parse(remapped.GetRawText())!;
        if (node is not JsonObject root || !root.TryGetPropertyValue("targetParams", out var tpNode)
            || tpNode is not JsonArray arr)
        {
            result = remapped;
            return;
        }

        foreach (var item in arr)
        {
            if (item is not JsonObject o || !o.TryGetPropertyValue("paramId", out var pidNode)
                || pidNode is not JsonValue pv)
            {
                continue;
            }

            var pid = pv.GetValue<string>();
            if (string.IsNullOrEmpty(pid))
            {
                continue;
            }

            if (targetById.TryGetValue(pid, out var meta))
            {
                o["paramName"] = meta.DisplayLabel;
            }
        }

        using var doc = JsonDocument.Parse(root.ToJsonString());
        result = doc.RootElement.Clone();
    }

    private async Task<JsonElement> NormalizeReferenceParamsAsync(
        JsonElement configJson,
        CancellationToken cancellationToken)
    {
        var (tasook, sat) = ReadScopeReference(configJson);
        var referenceParams = (await assetCacheRepository.GetParametersAsync(tasook, sat, cancellationToken)).ToArray();
        var byId = referenceParams.ToDictionary(p => p.ParamId, StringComparer.Ordinal);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        FilterTemplateConfigMapper.CollectParamIds(configJson, ids);

        foreach (var id in ids)
        {
            if (!byId.ContainsKey(id))
            {
                throw new TemplateGovernanceException(
                    TemplateErrorCodes.FilterTemplateConfigInvalid,
                    $"配置引用的参数在参考星 {tasook}/{sat} 的 param_cache 中不存在（请先完成资产同步，再选择参数）。缺失 ID：{id}");
            }
        }

        return FilterTemplateConfigMapper.EnrichTargetParamNames(configJson, byId);
    }

    private async Task EnsureReferenceSatelliteInGroupAsync(
        Guid groupId,
        JsonElement configJson,
        CancellationToken cancellationToken)
    {
        var (tasook, sat) = ReadScopeReference(configJson);
        if (!await groupService.IsSatelliteInGroupSubtreeAsync(groupId, tasook, sat, cancellationToken))
        {
            throw new TemplateGovernanceException(
                TemplateErrorCodes.FilterTemplateConfigInvalid,
                "参考卫星必须属于模板归属分组及其子分组下的成员（请先在分组中维护卫星成员）");
        }
    }

    private static (string TasookNo, string SatelliteNo) ReadScopeReference(JsonElement configJson)
    {
        if (!configJson.TryGetProperty("scope", out var scope) || scope.ValueKind != JsonValueKind.Object)
        {
            throw new TemplateGovernanceException(
                TemplateErrorCodes.FilterTemplateConfigInvalid,
                "config_json 缺少 scope");
        }

        var t = scope.TryGetProperty("referenceTasookNo", out var tn) && tn.ValueKind == JsonValueKind.String
            ? tn.GetString()
            : null;
        var s = scope.TryGetProperty("referenceSatelliteNo", out var sn) && sn.ValueKind == JsonValueKind.String
            ? sn.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(t) || string.IsNullOrWhiteSpace(s))
        {
            throw new TemplateGovernanceException(
                TemplateErrorCodes.FilterTemplateConfigInvalid,
                "scope 缺少 referenceTasookNo / referenceSatelliteNo");
        }

        return (t.Trim(), s.Trim());
    }

    private async Task EnsureGroupExistsAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var group = await groupRepository.GetByIdAsync(groupId, cancellationToken);
        if (group is null)
        {
            throw new TemplateGovernanceException(TemplateErrorCodes.GroupNotFound, "归属分组不存在");
        }
    }

    private static JsonElement NormalizeConfigJson(JsonElement raw, Guid groupId, SatelliteGroup? group)
    {
        // 将 scope.groupId / groupPath 与列字段保持一致
        using var doc = JsonDocument.Parse(raw.GetRawText());
        var dict = JsonElementToDictionary(doc.RootElement) ?? new Dictionary<string, object?>();

        var scope = dict.TryGetValue("scope", out var scopeNode) && scopeNode is Dictionary<string, object?> map
            ? map
            : new Dictionary<string, object?>();
        scope["groupId"] = groupId.ToString();
        if (group is not null)
        {
            scope["groupPath"] = group.GroupPath;
        }
        dict["scope"] = scope;

        var json = JsonSerializer.SerializeToDocument(dict);
        return json.RootElement.Clone();
    }

    private static Dictionary<string, object?>? JsonElementToDictionary(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var dict = new Dictionary<string, object?>();
        foreach (var prop in element.EnumerateObject())
        {
            dict[prop.Name] = JsonElementToObject(prop.Value);
        }
        return dict;
    }

    private static object? JsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => JsonElementToDictionary(element),
            JsonValueKind.Array => element.EnumerateArray().Select(JsonElementToObject).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }

    private static FilterTemplateView ToView(
        FilterTemplate template,
        IReadOnlyDictionary<Guid, SatelliteGroup> groupMap)
    {
        var path = groupMap.TryGetValue(template.GroupId, out var group) ? group.GroupPath : string.Empty;
        return new FilterTemplateView(
            template.TemplateId,
            template.Version,
            template.TemplateName,
            template.Status,
            template.GroupId,
            path,
            template.Description,
            template.CreatedAt,
            template.UpdatedAt,
            template.PublishedAt);
    }
}
