using SatelliteData.Domain.Assets;

namespace SatelliteData.Application.Assets;

internal static class SatelliteListItemMapper
{
    public static SatelliteListItem ToListItem(
        SatelliteCache satellite,
        IReadOnlyList<string> developmentPhases) =>
        new(
            satellite.TasookNo,
            satellite.TasookName,
            satellite.SatelliteNo,
            satellite.SatelliteName,
            satellite.DbStage,
            satellite.MongoInfo,
            satellite.SourceVersion,
            satellite.LastSyncedAt,
            satellite.CachedParameterCount,
            satellite.CachedCommandCount,
            developmentPhases);
}
