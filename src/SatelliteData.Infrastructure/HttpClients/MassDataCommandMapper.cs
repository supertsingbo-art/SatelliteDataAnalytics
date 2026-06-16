using System.Text.Json;
using SatelliteData.Domain.Assets;

namespace SatelliteData.Infrastructure.HttpClients;

/// <summary>
/// 将海量 <c>POST /api/v2/mass-data/basic/commands</c> 单条记录映射为 <see cref="CommandCache"/>。
/// 文档主字段落结构化列；其余字段保留在 <see cref="CommandCache.RawJson"/>。
/// </summary>
internal static class MassDataCommandMapper
{
    public static CommandCache? Map(
        JsonElement item,
        string tasookNo,
        string satelliteNo,
        string? sourceVersion,
        DateTimeOffset syncedAt)
    {
        var cmdId = item.GetIntOrNull("cmdId");
        if (!cmdId.HasValue)
        {
            return null;
        }

        return new CommandCache(
            tasookNo,
            satelliteNo,
            cmdId.Value,
            item.GetStringOrNull("cmdCode"),
            item.GetStringOrNull("cmdDesc"),
            item.GetIntOrNull("cmdType"),
            item.GetIntOrNull("cmdLen"),
            item.GetIntOrNull("exeTime"),
            item.GetIntOrNull("validFlag"),
            item.GetIntOrNull("cmdSysId"),
            sourceVersion,
            syncedAt,
            item.Clone());
    }
}
