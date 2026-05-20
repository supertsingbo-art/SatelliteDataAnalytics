using System.Text.Json;

namespace SatelliteData.Application.Pipeline;

/// <summary>
/// 目标参数离群判定配置，来源于筛选模板 <c>targetParams[].outlier</c>。
/// </summary>
public sealed record OutlierDetectionOptions(
    string Method,
    double? Min,
    double? Max,
    double SigmaK,
    int WindowSize)
{
    public static OutlierDetectionOptions DefaultSigma { get; } = new("SIGMA", null, null, 3d, 30);

    public static OutlierDetectionOptions Parse(JsonElement targetParamItem)
    {
        if (!targetParamItem.TryGetProperty("outlier", out var outlier)
            || outlier.ValueKind != JsonValueKind.Object)
        {
            return DefaultSigma;
        }

        var method = outlier.TryGetProperty("method", out var methodNode)
                       && methodNode.ValueKind == JsonValueKind.String
            ? methodNode.GetString() ?? "SIGMA"
            : "SIGMA";

        return new OutlierDetectionOptions(
            method.Trim().ToUpperInvariant(),
            ReadOptionalDouble(outlier, "min"),
            ReadOptionalDouble(outlier, "max"),
            ReadOptionalDouble(outlier, "sigma") ?? 3d,
            ReadOptionalInt(outlier, "windowSize") ?? 30);
    }

    private static double? ReadOptionalDouble(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var node))
        {
            return null;
        }

        return node.ValueKind switch
        {
            JsonValueKind.Number => node.GetDouble(),
            JsonValueKind.String when double.TryParse(node.GetString(), out var d) => d,
            _ => null
        };
    }

    private static int? ReadOptionalInt(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return node.TryGetInt32(out var v) ? Math.Max(1, v) : null;
    }
}
