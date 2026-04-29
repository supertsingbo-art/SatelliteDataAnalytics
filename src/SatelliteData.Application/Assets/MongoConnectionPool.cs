using SatelliteData.Domain.Assets;

namespace SatelliteData.Application.Assets;

public sealed class MongoConnectionPool(IMassDataAssetProvider massDataProvider)
{
    private readonly Dictionary<(string TasookNo, string SatelliteNo), MongoConnectionInfo> _cache = [];

    public async Task<MongoConnectionInfo> GetConnectionInfoAsync(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken)
    {
        var key = (tasookNo, satelliteNo);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var mongoInfo = await massDataProvider.GetMongoInfoAsync(tasookNo, satelliteNo, cancellationToken)
            ?? throw new InvalidOperationException($"Mongo info not found for {tasookNo}/{satelliteNo}.");

        _cache[key] = mongoInfo;
        return mongoInfo;
    }

    public void Invalidate(string tasookNo, string satelliteNo)
    {
        _cache.Remove((tasookNo, satelliteNo));
    }
}
