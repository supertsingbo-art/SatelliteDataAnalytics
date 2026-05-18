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
            source.CmdDesc,
            source.CmdType,
            source.CmdLen,
            source.ExeTime,
            source.ValidFlag,
            source.CmdSysId,
            source.SourceVersion,
            source.LastSyncedAt);
}
