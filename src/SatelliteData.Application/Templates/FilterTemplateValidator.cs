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
        ">", ">=", "<", "<=", "=", "==", "!=", "between"
    };
    private static readonly HashSet<string> AllowedConditionOperators = new(StringComparer.Ordinal)
    {
        ">", ">=", "<", "<=", "=", "!=", "between"
    };
    private static readonly HashSet<string> AllowedRuleLogics = new(StringComparer.Ordinal) { "AND", "OR", "NOT" };
    private static readonly HashSet<string> AllowedInstructionRelations = new(StringComparer.Ordinal) { "AND", "OR" };
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
        var hasConditionConfig = configJson.TryGetProperty("conditionConfig", out var conditionConfig)
                                 && conditionConfig.ValueKind == JsonValueKind.Object;
        if (hasConditionConfig)
        {
            ValidateConditionConfig(conditionConfig);
        }
        else
        {
            throw Invalid("conditionConfig 必须存在且为对象");
        }

        ValidateDuration(configJson);
        ValidateTargetParams(configJson);
    }

    private static void ValidateConditionConfig(JsonElement conditionConfig)
    {
        ValidateInstructionConfig(conditionConfig);
        ValidateBoolean(conditionConfig, "parametersEnabled", "conditionConfig.parametersEnabled");

        var conditionIds = new HashSet<string>(StringComparer.Ordinal);
        if (conditionConfig.TryGetProperty("parameters", out var parameters))
        {
            if (parameters.ValueKind != JsonValueKind.Array)
            {
                throw Invalid("conditionConfig.parameters 必须是数组");
            }

            foreach (var item in parameters.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    throw Invalid("conditionConfig.parameters[] 必须是对象");
                }

                if (!item.TryGetProperty("conditionId", out var conditionIdNode)
                    || conditionIdNode.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(conditionIdNode.GetString()))
                {
                    throw Invalid("conditionConfig.parameters[].conditionId 必须是非空字符串");
                }

                var conditionId = conditionIdNode.GetString()!.Trim();
                if (!conditionIds.Add(conditionId))
                {
                    throw Invalid($"conditionConfig.parameters 存在重复 conditionId: {conditionId}");
                }

                if (!item.TryGetProperty("paramId", out var paramIdNode)
                    || paramIdNode.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(paramIdNode.GetString()))
                {
                    throw Invalid("conditionConfig.parameters[].paramId 必须是非空字符串");
                }

                if (!item.TryGetProperty("operator", out var opNode)
                    || opNode.ValueKind != JsonValueKind.String)
                {
                    throw Invalid("conditionConfig.parameters[].operator 必须存在");
                }

                var op = opNode.GetString() ?? "";
                if (!AllowedConditionOperators.Contains(op))
                {
                    throw Invalid($"conditionConfig.parameters[].operator '{op}' 不在允许列表 [{string.Join(',', AllowedConditionOperators)}]");
                }

                if (!item.TryGetProperty("value", out var value)
                    || (value.ValueKind != JsonValueKind.Number && value.ValueKind != JsonValueKind.String && value.ValueKind != JsonValueKind.Array))
                {
                    throw Invalid("conditionConfig.parameters[].value 必须是 number/string/array");
                }

                if (string.Equals(op, "between", StringComparison.Ordinal)
                    && (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 2))
                {
                    throw Invalid("conditionConfig.parameters[].operator='between' 时，value 必须为长度 2 的数组");
                }
            }
        }

        var expression = conditionConfig.TryGetProperty("expression", out var expNode) && expNode.ValueKind == JsonValueKind.String
            ? (expNode.GetString() ?? "").Trim()
            : string.Empty;
        if (string.IsNullOrWhiteSpace(expression))
        {
            return;
        }

        if (conditionIds.Count == 0)
        {
            throw Invalid("conditionConfig.expression 不为空时，conditionConfig.parameters 不能为空");
        }

        if (!ConditionExpressionParser.TryParseToPostfix(expression, out var postfix, out var parseError))
        {
            throw Invalid($"conditionConfig.expression 语法错误: {parseError}");
        }

        if (!ConditionExpressionParser.ValidateIdentifiers(postfix, conditionIds, out var idError))
        {
            throw Invalid($"conditionConfig.expression 校验失败: {idError}");
        }
    }

    private static void ValidateInstructionConfig(JsonElement conditionConfig)
    {
        if (!conditionConfig.TryGetProperty("instructions", out var ins))
        {
            return;
        }

        if (ins.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("conditionConfig.instructions 必须是对象");
        }

        ValidateBoolean(ins, "enabled", "conditionConfig.instructions.enabled");
        ValidateInstructionRelation(ins, "startRelation");
        ValidateInstructionRelation(ins, "endRelation");
        ValidateInstructionRange(ins, "startRangeSeconds");
        ValidateInstructionRange(ins, "endRangeSeconds");
        ValidateInstructionCommandArray(ins, "startCommands");
        ValidateInstructionCommandArray(ins, "endCommands");
    }

    private static void ValidateInstructionRange(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value))
        {
            return;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var seconds) || seconds < 0)
        {
            throw Invalid($"conditionConfig.instructions.{property} 必须是非负整数");
        }
    }

    private static void ValidateInstructionRelation(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value))
        {
            return;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"conditionConfig.instructions.{property} 必须是字符串");
        }

        var relation = value.GetString() ?? "";
        if (!AllowedInstructionRelations.Contains(relation))
        {
            throw Invalid($"conditionConfig.instructions.{property} 仅允许 AND / OR");
        }
    }

    private static void ValidateInstructionCommandArray(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var commands))
        {
            return;
        }

        if (commands.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"conditionConfig.instructions.{property} 必须是数组");
        }

        foreach (var item in commands.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw Invalid($"conditionConfig.instructions.{property}[] 必须是对象");
            }

            if (!item.TryGetProperty("conditionId", out var conditionIdNode)
                || conditionIdNode.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(conditionIdNode.GetString()))
            {
                throw Invalid($"conditionConfig.instructions.{property}[].conditionId 必须是非空字符串");
            }

            if (!item.TryGetProperty("commandId", out var commandId))
            {
                throw Invalid($"conditionConfig.instructions.{property}[].commandId 必须存在");
            }

            if (commandId.ValueKind != JsonValueKind.String && commandId.ValueKind != JsonValueKind.Number)
            {
                throw Invalid($"conditionConfig.instructions.{property}[].commandId 必须是字符串或数字");
            }

            if (item.TryGetProperty("channelId", out var channelId)
                && (channelId.ValueKind != JsonValueKind.Number || channelId.GetInt32() < 0))
            {
                throw Invalid($"conditionConfig.instructions.{property}[].channelId 必须是非负整数");
            }
        }
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

        if (!scope.TryGetProperty("referenceTasookNo", out var refT) || refT.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(refT.GetString()))
        {
            throw Invalid("scope.referenceTasookNo 必须为非空字符串（参考卫星型号代号）");
        }

        if (!scope.TryGetProperty("referenceSatelliteNo", out var refS) || refS.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(refS.GetString()))
        {
            throw Invalid("scope.referenceSatelliteNo 必须为非空字符串（参考卫星代号）");
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

            if (!node.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array)
            {
                throw Invalid("ruleTree 逻辑算子节点必须包含 children 数组");
            }

            if (children.GetArrayLength() == 0)
            {
                if (!string.Equals(op, "AND", StringComparison.Ordinal))
                {
                    throw Invalid("无参数条件时 ruleTree 仅允许 op=AND 且 children 为空");
                }

                return;
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

    private static void ValidateBoolean(JsonElement parent, string property, string fieldName)
    {
        if (!parent.TryGetProperty(property, out var node))
        {
            return;
        }

        if (node.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw Invalid($"{fieldName} 必须是布尔值");
        }
    }

    private static TemplateGovernanceException Invalid(string message)
    {
        return new TemplateGovernanceException(TemplateErrorCodes.FilterTemplateConfigInvalid, message);
    }
}
