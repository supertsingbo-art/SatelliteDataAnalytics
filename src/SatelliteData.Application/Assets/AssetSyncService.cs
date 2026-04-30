using SatelliteData.Domain.Assets;

namespace SatelliteData.Application.Assets;

public sealed class AssetSyncService(
    IMassDataAssetProvider massDataProvider,
    ISatelliteAssetProvider satelliteAssetProvider,
    IAssetCacheRepository cacheRepository)
{
    public async Task<AssetSyncResult> SyncAllAsync(CancellationToken cancellationToken)
    {
        var satellites = await massDataProvider.GetSatellitesAsync(cancellationToken);
        var parameterCount = 0;
        var testBatchCount = 0;

        foreach (var satellite in satellites)
        {
            var result = await SyncSatelliteAsync(satellite.TasookNo, satellite.SatelliteNo, cancellationToken);
            parameterCount += result.ParameterCount;
            testBatchCount += result.TestBatchCount;
        }

        return new AssetSyncResult(satellites.Count, parameterCount, testBatchCount, DateTimeOffset.UtcNow);
    }

    public async Task<AssetSyncResult> SyncSatelliteAsync(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken)
    {
        var satellites = await massDataProvider.GetSatellitesAsync(cancellationToken);
        var satellite = satellites.SingleOrDefault(item =>
            string.Equals(item.TasookNo, tasookNo, StringComparison.Ordinal)
            && string.Equals(item.SatelliteNo, satelliteNo, StringComparison.Ordinal));

        var parameters = await massDataProvider.GetParametersAsync(tasookNo, satelliteNo, cancellationToken);
        var mongoInfo = await massDataProvider.GetMongoInfoAsync(tasookNo, satelliteNo, cancellationToken);
        var testBatches = await satelliteAssetProvider.GetTestBatchesAsync(
            tasookNo,
            satelliteNo,
            null,
            null,
            cancellationToken);

        if (satellite is not null)
        {
            await cacheRepository.UpsertSatelliteAsync(satellite with { MongoInfo = mongoInfo }, cancellationToken);
        }

        await cacheRepository.UpsertParametersAsync(parameters, cancellationToken);
        await cacheRepository.UpsertTestBatchesAsync(testBatches, cancellationToken);

        return new AssetSyncResult(satellite is null ? 0 : 1, parameters.Count, testBatches.Count, DateTimeOffset.UtcNow);
    }

    public async Task RefreshIfExpiredAsync(
        string tasookNo,
        string satelliteNo,
        TimeSpan maxAge,
        CancellationToken cancellationToken)
    {
        var cached = await cacheRepository.GetSatelliteAsync(tasookNo, satelliteNo, cancellationToken);
        if (cached is null || DateTimeOffset.UtcNow - cached.LastSyncedAt > maxAge)
        {
            await SyncSatelliteAsync(tasookNo, satelliteNo, cancellationToken);
        }
    }

    public Task ClearAllCacheAsync(CancellationToken cancellationToken)
    {
        return cacheRepository.ClearAsync(cancellationToken);
    }
}
