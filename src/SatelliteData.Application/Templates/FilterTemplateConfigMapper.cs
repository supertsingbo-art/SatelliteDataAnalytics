using System.Text.Json;
using System.Text.Json.Nodes;
using SatelliteData.Domain.Assets;

namespace SatelliteData.Application.Templates;

/// <summary>
/// 收集筛选模板中出现的 paramId，并在 JSON 树上做就地替换（用于跨星语义映射）。
/// </summary>
public static class FilterTemplateConfigMapper
{
    public static void CollectParamIds(JsonElement config, HashSet<string> destination)
    {
        if (config.TryGetProperty("ruleTree", out var ruleTree))
        {
            CollectFromRuleNode(ruleTree, destination);
        }

        if (config.TryGetProperty("targetParams", out var targetParams) &&
            targetParams.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in targetParams.EnumerateArray())
            {
                if (item.TryGetProperty("paramId", out var pid) && pid.ValueKind == JsonValueKind.String)
                {
                    var s = pid.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        destination.Add(s);
                    }
                }
            }
        }
    }

    public static JsonElement ApplyParamIdMap(JsonElement config, IReadOnlyDictionary<string, string> map)
    {
        if (map.Count == 0)
        {
            return config.Clone();
        }

        var node = JsonNode.Parse(config.GetRawText())!;
        RewriteParamIds(node, map);
        using var doc = JsonDocument.Parse(node.ToJsonString());
        return doc.RootElement.Clone();
    }

    private static void CollectFromRuleNode(JsonElement node, HashSet<string> destination)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (node.TryGetProperty("paramId", out var pid) && pid.ValueKind == JsonValueKind.String)
        {
            var s = pid.GetString();
            if (!string.IsNullOrWhiteSpace(s))
            {
                destination.Add(s);
            }
        }

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                CollectFromRuleNode(child, destination);
            }
        }
    }

    private static void RewriteParamIds(JsonNode? node, IReadOnlyDictionary<string, string> map)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj.TryGetPropertyValue("paramId", out var pidNode) &&
                    pidNode is JsonValue pv)
                {
                    var pid = pv.GetValue<string>();
                    if (!string.IsNullOrEmpty(pid) && map.TryGetValue(pid, out var mapped))
                    {
                        obj["paramId"] = mapped;
                    }
                }

                foreach (var property in obj)
                {
                    RewriteParamIds(property.Value, map);
                }

                break;
            case JsonArray arr:
                foreach (var item in arr)
                {
                    RewriteParamIds(item, map);
                }

                break;
        }
    }

    public static string NormalizeLabel(string? text)
    {
        return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
    }

    public static string? TryReadDescription(JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in new[] { "description", "desc", "paramDesc", "remark", "memo" })
        {
            if (raw.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
            {
                var s = v.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    return s;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 将参考星上的 paramId 映射到目标星：优先同 ID；否则按参数名称（忽略大小写）；再否则按原始 JSON 中描述类字段。
    /// </summary>
    public static string? MapParamId(
        string referenceParamId,
        IReadOnlyDictionary<string, ParamCache> referenceById,
        IReadOnlyList<ParamCache> targetParams,
        List<string> warnings)
    {
        if (targetParams.Any(p => string.Equals(p.ParamId, referenceParamId, StringComparison.Ordinal)))
        {
            return referenceParamId;
        }

        if (!referenceById.TryGetValue(referenceParamId, out var refParam))
        {
            warnings.Add($"参考星缺少 param_id={referenceParamId} 的缓存元数据，无法做语义映射");
            return null;
        }

        var refNameKey = NormalizeLabel(refParam.ParaCode ?? refParam.ParamName);
        var refDescKey = NormalizeLabel(refParam.ParaDesc);
        var nameMatches = targetParams
            .Where(p => string.Equals(NormalizeLabel(p.ParaCode ?? p.ParamName), refNameKey, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (nameMatches.Length == 1)
        {
            return nameMatches[0].ParamId;
        }

        if (nameMatches.Length > 1)
        {
            var picked = nameMatches[0].ParamId;
            warnings.Add(
                $"参数「{refParam.ParamName}」在目标星存在多个同名匹配，已选用 param_id={picked}；请人工核对");
            return picked;
        }

        var refDesc = refDescKey.Length > 0 ? refDescKey : NormalizeLabel(TryReadDescription(refParam.RawJson));
        if (!string.IsNullOrWhiteSpace(refDesc))
        {
            var descMatches = targetParams
                .Where(p =>
                {
                    var targetDesc = NormalizeLabel(p.ParaDesc ?? TryReadDescription(p.RawJson));
                    return targetDesc.Length > 0
                           && string.Equals(targetDesc, refDesc, StringComparison.OrdinalIgnoreCase);
                })
                .ToArray();
            if (descMatches.Length == 1)
            {
                return descMatches[0].ParamId;
            }

            if (descMatches.Length > 1)
            {
                var picked = descMatches[0].ParamId;
                warnings.Add(
                    $"参数描述与「{refDesc}」在目标星存在多条匹配，已选用 param_id={picked}；请人工核对");
                return picked;
            }
        }

        warnings.Add(
            $"无法在目标星找到与参考 param_id={referenceParamId}（{refParam.ParamName}）对应的参数（名称/描述语义匹配失败）");
        return null;
    }
}
