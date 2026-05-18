namespace SatelliteData.Domain.Assets;

/// <summary>
/// 参数缓存的人类可读标签（与前端 <c>formatParamCacheLabel</c> 一致：代号 + 描述，不含 paraId）。
/// </summary>
public static class ParamCacheLabels
{
    public static string FormatDisplayLabel(string? paraCode, string? paraDesc, int paraId)
    {
        var code = paraCode?.Trim();
        var desc = paraDesc?.Trim();
        if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(desc))
        {
            return $"{code} {desc}";
        }

        if (!string.IsNullOrEmpty(code))
        {
            return code;
        }

        if (!string.IsNullOrEmpty(desc))
        {
            return desc;
        }

        return paraId.ToString();
    }
}
