using System.Text.Json;
using SatelliteData.Domain.Assets;

namespace SatelliteData.Infrastructure.HttpClients;

/// <summary>
/// 将海量 <c>POST /api/v2/mass-data/basic/parameters</c> 单条记录映射为 <see cref="ParamCache"/>。
/// 文档主字段落 <see cref="ParamCache"/> 各列；其余字段保留在 <see cref="ParamCache.RawJson"/>。
/// </summary>
internal static class MassDataParameterMapper
{
    public static ParamCache? Map(
        JsonElement item,
        string tasookNo,
        string satelliteNo,
        string? sourceVersion,
        DateTimeOffset syncedAt)
    {
        var paraId = item.GetIntOrNull("paraId");
        if (!paraId.HasValue)
        {
            return null;
        }

        return new ParamCache(
            tasookNo,
            satelliteNo,
            paraId.Value,
            item.GetStringOrNull("paraCode"),
            item.GetStringOrNull("paraDesc"),
            item.GetStringOrNull("paraTypeDesc"),
            item.GetDoubleOrNull("minValue"),
            item.GetDoubleOrNull("maxValue"),
            item.GetIntOrNull("updateTime"),
            item.GetStringOrNull("procDesc"),
            item.GetIntOrNull("prmSysId"),
            sourceVersion,
            syncedAt,
            item.Clone());
    }
}
