using System.Text.Json;

namespace SatelliteData.Application.Templates;

/// <summary>
/// 筛选模板配置静态校验。校验 6.2.3.4 所规定的 <c>config_json</c> 结构：scope / timeWindow / ruleTree / durationSeconds / targetParams。
/// 校验失败时抛出 <see cref="TemplateGovernanceException"/>，错误码为 <c>TPL_004</c>。
/// </summary>
public static class FilterTemplateValidator
{
    private static readonly HashSet<string> AllowedTimeWindowModes = new(StringComparer.Ordinal) { "TEST_BATCH", "CUSTOM" };
    private static readonly HashSet<string> AllowedRuleOperators = new(StringComparer.Ordinal)
    {
        ">", ">=", "<", "<=", "==", "!=", "between"
    };
    private static readonly HashSet<string> AllowedRuleLogics = new(StringComparer.Ordinal) { "AND", "OR", "NOT" };
    private static readonly HashSet<string> AllowedOutlierMethods = new(StringComparer.Ordinal)
    {
        "THRESHOLD", "SIGMA", "IQR", "MAD", "HAMPEL"
    };

    public static void Validate(JsonElement configJson)
    {
        if (configJson.ValueKind != JsonValueKind.Object)
        {
            throw new TemplateGovernanceException(
                TemplateErrorCodes.FilterTemplateConfigInvalid,
                "config_json 必须是对象");
        }

        ValidateScope(configJson);
        ValidateTimeWindow(configJson);
        ValidateRuleTree(configJson);
        ValidateDuration(configJson);
        ValidateTargetParams(configJson);
    }

    private static void ValidateScope(JsonElement root)
    {
        if (!root.TryGetProperty("scope", out var scope) || scope.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("scope 节点必须存在");
        }

        if (!scope.TryGetProperty("groupId", out var groupId) || groupId.ValueKind != JsonValueKind.String)
        {
            throw Invalid("scope.groupId 必须为字符串 (UUID)");
        }
    }

    private static void ValidateTimeWindow(JsonElement root)
    {
        if (!root.TryGetProperty("timeWindow", out var timeWindow) || timeWindow.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("timeWindow 节点必须存在");
        }

        var mode = timeWindow.TryGetProperty("mode", out var modeNode) && modeNode.ValueKind == JsonValueKind.String
            ? modeNode.GetString() ?? ""
            : "";
        if (!AllowedTimeWindowModes.Contains(mode))
        {
            throw Invalid("timeWindow.mode 仅允许 TEST_BATCH / CUSTOM");
        }

        EnsureNonNegativeInt(timeWindow, "bufferBeforeSeconds", optional: true);
        EnsureNonNegativeInt(timeWindow, "bufferAfterSeconds", optional: true);
    }

    private static void ValidateRuleTree(JsonElement root)
    {
        if (!root.TryGetProperty("ruleTree", out var ruleTree))
        {
            throw Invalid("ruleTree 节点必须存在");
        }

        ValidateRuleNode(ruleTree, depth: 0);
    }

    private static void ValidateRuleNode(JsonElement node, int depth)
    {
        if (depth > 8)
        {
            throw Invalid("ruleTree 嵌套层级不得超过 8 层");
        }

        if (node.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("ruleTree 子节点必须是对象");
        }

        if (node.TryGetProperty("op", out var opNode) && opNode.ValueKind == JsonValueKind.String)
        {
            var op = opNode.GetString() ?? "";
            if (!AllowedRuleLogics.Contains(op))
            {
                throw Invalid($"ruleTree 逻辑算子 op 必须是 AND/OR/NOT，当前 '{op}'");
            }

            if (!node.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array || children.GetArrayLength() == 0)
            {
                throw Invalid("ruleTree 逻辑算子节点必须包含非空 children 数组");
            }

            foreach (var child in children.EnumerateArray())
            {
                ValidateRuleNode(child, depth + 1);
            }
            return;
        }

        // 叶子节点：参数条件
        if (!node.TryGetProperty("paramId", out var paramId) || paramId.ValueKind != JsonValueKind.String)
        {
            throw Invalid("ruleTree 叶子节点必须包含 paramId 字符串");
        }

        if (!node.TryGetProperty("operator", out var op2) || op2.ValueKind != JsonValueKind.String)
        {
            throw Invalid("ruleTree 叶子节点必须包含 operator");
        }
        var opStr = op2.GetString() ?? "";
        if (!AllowedRuleOperators.Contains(opStr))
        {
            throw Invalid($"ruleTree.operator '{opStr}' 不在允许列表 [{string.Join(',', AllowedRuleOperators)}]");
        }

        if (!node.TryGetProperty("value", out var value)
            || (value.ValueKind != JsonValueKind.Number && value.ValueKind != JsonValueKind.String && value.ValueKind != JsonValueKind.Array))
        {
            throw Invalid("ruleTree 叶子节点必须包含 value (number/string/array)");
        }

        if (string.Equals(opStr, "between", StringComparison.Ordinal))
        {
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 2)
            {
                throw Invalid("operator='between' 的 value 必须是长度为 2 的数组");
            }
        }
    }

    private static void ValidateDuration(JsonElement root)
    {
        if (root.TryGetProperty("durationSeconds", out var duration))
        {
            if (duration.ValueKind != JsonValueKind.Number || duration.GetInt32() < 0)
            {
                throw Invalid("durationSeconds 必须是非负整数");
            }
        }
    }

    private static void ValidateTargetParams(JsonElement root)
    {
        if (!root.TryGetProperty("targetParams", out var targetParams) || targetParams.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("targetParams 必须是数组");
        }

        if (targetParams.GetArrayLength() == 0)
        {
            throw Invalid("至少需要 1 个 targetParams");
        }

        var paramIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in targetParams.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("targetParams 元素必须是对象");
            }

            if (!item.TryGetProperty("paramId", out var paramIdNode) || paramIdNode.ValueKind != JsonValueKind.String)
            {
                throw Invalid("targetParams[].paramId 必须存在且为字符串");
            }

            var paramId = paramIdNode.GetString() ?? "";
            if (!paramIds.Add(paramId))
            {
                throw Invalid($"targetParams 中存在重复的 paramId: {paramId}");
            }

            if (item.TryGetProperty("outlier", out var outlier))
            {
                if (outlier.ValueKind != JsonValueKind.Object)
                {
                    throw Invalid("targetParams[].outlier 必须是对象");
                }
                if (!outlier.TryGetProperty("method", out var methodNode) || methodNode.ValueKind != JsonValueKind.String)
                {
                    throw Invalid("targetParams[].outlier.method 必须存在");
                }
                var method = methodNode.GetString() ?? "";
                if (!AllowedOutlierMethods.Contains(method))
                {
                    throw Invalid($"targetParams[].outlier.method '{method}' 不在允许列表 [{string.Join(',', AllowedOutlierMethods)}]");
                }
            }

            EnsureNonNegativeInt(item, "boundaryBufferBeforeSec", optional: true);
            EnsureNonNegativeInt(item, "boundaryBufferAfterSec", optional: true);
        }
    }

    private static void EnsureNonNegativeInt(JsonElement parent, string property, bool optional)
    {
        if (!parent.TryGetProperty(property, out var node))
        {
            if (!optional)
            {
                throw Invalid($"{property} 必须存在");
            }
            return;
        }

        if (node.ValueKind != JsonValueKind.Number || node.GetInt32() < 0)
        {
            throw Invalid($"{property} 必须是非负整数");
        }
    }

    private static TemplateGovernanceException Invalid(string message)
    {
        return new TemplateGovernanceException(TemplateErrorCodes.FilterTemplateConfigInvalid, message);
    }
}
