using System.Text.Json;

namespace SatelliteData.Application.Templates;

public sealed record InstructionConditionItem(
    string ConditionId,
    string CommandId,
    int ChannelId);

public sealed record ParameterConditionItem(
    string ConditionId,
    string ParamId,
    string Operator,
    JsonElement Value);

public sealed record FilterConditionConfig(
    IReadOnlyList<InstructionConditionItem> StartCommands,
    IReadOnlyList<InstructionConditionItem> EndCommands,
    string StartRelation,
    string EndRelation,
    IReadOnlyList<ParameterConditionItem> Parameters,
    string Expression,
    int StartRangeSeconds = 0,
    int EndRangeSeconds = 0);

public static class ConditionConfigParser
{
    public static bool TryParse(JsonElement configJson, out FilterConditionConfig? conditionConfig)
    {
        conditionConfig = null;
        if (!configJson.TryGetProperty("conditionConfig", out var cc) || cc.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var instructions = cc.TryGetProperty("instructions", out var ins) && ins.ValueKind == JsonValueKind.Object
            ? ins
            : default;
        var startRelation = ReadRelation(instructions, "startRelation", "OR");
        var endRelation = ReadRelation(instructions, "endRelation", "OR");
        var startRangeSeconds = ReadNonNegativeInt(instructions, "startRangeSeconds");
        var endRangeSeconds = ReadNonNegativeInt(instructions, "endRangeSeconds");
        var startCommands = ParseCommands(instructions, "startCommands", "S");
        var endCommands = ParseCommands(instructions, "endCommands", "E");
        var parameters = ParseParameters(cc);
        var expression = cc.TryGetProperty("expression", out var exp) && exp.ValueKind == JsonValueKind.String
            ? exp.GetString() ?? string.Empty
            : string.Empty;

        conditionConfig = new FilterConditionConfig(
            startCommands,
            endCommands,
            startRelation,
            endRelation,
            parameters,
            expression.Trim(),
            startRangeSeconds,
            endRangeSeconds);
        return true;
    }

    private static string ReadRelation(JsonElement parent, string property, string fallback)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(property, out var node)
            && node.ValueKind == JsonValueKind.String)
        {
            var value = (node.GetString() ?? string.Empty).Trim().ToUpperInvariant();
            if (value is "AND" or "OR")
            {
                return value;
            }
        }

        return fallback;
    }

    private static int ReadNonNegativeInt(JsonElement parent, string property)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(property, out var node)
            && node.ValueKind == JsonValueKind.Number
            && node.TryGetInt32(out var number))
        {
            return Math.Max(0, number);
        }

        return 0;
    }

    private static IReadOnlyList<InstructionConditionItem> ParseCommands(
        JsonElement parent,
        string property,
        string prefix)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(property, out var arr)
            || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<InstructionConditionItem>();
        }

        var result = new List<InstructionConditionItem>();
        var index = 0;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                index++;
                continue;
            }

            var commandId = ReadStringOrNumber(item, "commandId");
            if (string.IsNullOrWhiteSpace(commandId))
            {
                index++;
                continue;
            }

            var conditionId = ReadStringOrNumber(item, "conditionId");
            if (string.IsNullOrWhiteSpace(conditionId))
            {
                conditionId = $"{prefix}{index + 1}";
            }

            var channelId = 0;
            if (item.TryGetProperty("channelId", out var ch) && ch.ValueKind == JsonValueKind.Number)
            {
                _ = ch.TryGetInt32(out channelId);
            }

            result.Add(new InstructionConditionItem(
                conditionId.Trim(),
                commandId.Trim(),
                channelId));
            index++;
        }

        return result;
    }

    private static IReadOnlyList<ParameterConditionItem> ParseParameters(JsonElement parent)
    {
        if (!parent.TryGetProperty("parameters", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ParameterConditionItem>();
        }

        var result = new List<ParameterConditionItem>();
        var index = 0;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                index++;
                continue;
            }

            var paramId = ReadStringOrNumber(item, "paramId");
            if (string.IsNullOrWhiteSpace(paramId))
            {
                index++;
                continue;
            }

            var conditionId = ReadStringOrNumber(item, "conditionId");
            if (string.IsNullOrWhiteSpace(conditionId))
            {
                conditionId = $"P{index + 1}";
            }

            var op = item.TryGetProperty("operator", out var opNode) && opNode.ValueKind == JsonValueKind.String
                ? (opNode.GetString() ?? string.Empty).Trim()
                : string.Empty;
            if (string.IsNullOrWhiteSpace(op))
            {
                index++;
                continue;
            }

            if (!item.TryGetProperty("value", out var value))
            {
                index++;
                continue;
            }

            result.Add(new ParameterConditionItem(
                conditionId.Trim(),
                paramId.Trim(),
                op,
                Clone(value)));
            index++;
        }

        return result;
    }

    private static string? ReadStringOrNumber(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.TryGetInt64(out var l) ? l.ToString() : value.GetDouble().ToString("G"),
            _ => null
        };
    }

    private static JsonElement Clone(JsonElement value)
    {
        using var doc = JsonDocument.Parse(value.GetRawText());
        return doc.RootElement.Clone();
    }
}
