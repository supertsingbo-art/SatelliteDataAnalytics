using SatelliteData.Domain.Assets;

namespace SatelliteData.Application.Assets;

internal static class ParamCacheViewMapper
{
    public static ParamCacheView ToView(ParamCache source) =>
        new(
            source.TasookNo,
            source.SatelliteNo,
            source.ParaId,
            source.ParaCode,
            source.ParaDesc,
            source.ParaTypeDesc,
            source.MinValue,
            source.MaxValue,
            source.UpdateTime,
            source.ProcDesc,
            source.PrmSysId,
            source.SourceVersion,
            source.LastSyncedAt);
}
