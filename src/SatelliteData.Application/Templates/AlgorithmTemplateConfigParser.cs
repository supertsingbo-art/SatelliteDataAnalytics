using System.Text.Json;

namespace SatelliteData.Application.Templates;

public enum SourceParamResolveFailure
{
    None,
    Missing,
    Multiple
}

public static class AlgorithmTemplateConfigParser
{
    public static bool TryResolveSourceParamId(
        JsonElement nodeData,
        string nodeId,
        JsonElement configJson,
        out string paramId,
        out SourceParamResolveFailure failure)
    {
        paramId = "";
        failure = SourceParamResolveFailure.None;

        if (TryReadLegacyParamId(nodeData, out paramId))
        {
            return true;
        }

        var nodeRef = ResolveNodeRef(nodeData, nodeId);
        if (!TryGetDataInput(configJson, nodeRef, out var dataInput))
        {
            failure = SourceParamResolveFailure.Missing;
            return false;
        }

        if (dataInput.ParamIds.Count == 0)
        {
            failure = SourceParamResolveFailure.Missing;
            return false;
        }

        if (dataInput.ParamIds.Count > 1)
        {
            failure = SourceParamResolveFailure.Multiple;
            return false;
        }

        paramId = dataInput.ParamIds[0];
        return !string.IsNullOrWhiteSpace(paramId);
    }

    public static bool TryValidateSourceParam(
        JsonElement nodeData,
        string nodeId,
        JsonElement configJson,
        out SourceParamResolveFailure failure)
    {
        if (TryReadLegacyParamId(nodeData, out _))
        {
            failure = SourceParamResolveFailure.None;
            return true;
        }

        var nodeRef = ResolveNodeRef(nodeData, nodeId);
        if (!TryGetDataInput(configJson, nodeRef, out var dataInput))
        {
            failure = SourceParamResolveFailure.Missing;
            return false;
        }

        if (dataInput.ParamIds.Count == 0)
        {
            failure = SourceParamResolveFailure.Missing;
            return false;
        }

        if (dataInput.ParamIds.Count > 1)
        {
            failure = SourceParamResolveFailure.Multiple;
            return false;
        }

        failure = SourceParamResolveFailure.None;
        return !string.IsNullOrWhiteSpace(dataInput.ParamIds[0]);
    }

    public static string ResolveNodeRef(JsonElement nodeData, string nodeId)
    {
        if (nodeData.ValueKind == JsonValueKind.Object
            && nodeData.TryGetProperty("nodeRef", out var nodeRefEl)
            && nodeRefEl.ValueKind == JsonValueKind.String)
        {
            var nodeRef = nodeRefEl.GetString();
            if (!string.IsNullOrWhiteSpace(nodeRef))
            {
                return nodeRef;
            }
        }

        return nodeId;
    }

    private static bool TryReadLegacyParamId(JsonElement nodeData, out string paramId)
    {
        paramId = "";
        if (nodeData.ValueKind != JsonValueKind.Object) return false;
        if (!nodeData.TryGetProperty("paramId", out var p) || p.ValueKind != JsonValueKind.String) return false;
        paramId = p.GetString() ?? "";
        return !string.IsNullOrWhiteSpace(paramId);
    }

    private static bool TryGetDataInput(JsonElement configJson, string nodeRef, out DataInputConfig dataInput)
    {
        dataInput = default;
        if (configJson.ValueKind != JsonValueKind.Object) return false;
        if (!configJson.TryGetProperty("dataInputs", out var dataInputs) || dataInputs.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in dataInputs.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!item.TryGetProperty("nodeRef", out var refEl) || refEl.ValueKind != JsonValueKind.String) continue;
            if (!string.Equals(refEl.GetString(), nodeRef, StringComparison.Ordinal)) continue;
            dataInput = ParseDataInput(item);
            return true;
        }

        return false;
    }

    private static DataInputConfig ParseDataInput(JsonElement item)
    {
        var paramIds = new List<string>();
        if (item.TryGetProperty("paramIds", out var ids) && ids.ValueKind == JsonValueKind.Array)
        {
            foreach (var id in ids.EnumerateArray())
            {
                if (id.ValueKind != JsonValueKind.String) continue;
                var value = id.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    paramIds.Add(value);
                }
            }
        }

        return new DataInputConfig(paramIds);
    }

    private readonly record struct DataInputConfig(IReadOnlyList<string> ParamIds);
}
