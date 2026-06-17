using SatelliteData.Domain.Assets;

namespace SatelliteData.Application.Assets;

internal static class CommandCacheViewMapper
{
    public static CommandCacheView ToView(CommandCache source) =>
        new(
            source.TasookNo,
            source.SatelliteNo,
            source.CmdId,
            source.CmdCode,
            TryReadCommandName(source),
            source.CmdDesc,
            source.CmdType,
            source.CmdLen,
            source.ExeTime,
            source.ValidFlag,
            source.CmdSysId,
            source.SourceVersion,
            source.LastSyncedAt);

    private static string? TryReadCommandName(CommandCache source)
    {
        var raw = source.RawJson;
        if (raw.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return null;
        }

        foreach (var key in new[] { "cmdName", "commandName", "name" })
        {
            if (raw.TryGetProperty(key, out var value)
                && value.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }
        }

        return null;
    }
}
