namespace SatelliteData.Application.Pipeline;

public sealed record RawSeriesPoint(DateTimeOffset Ts, double Value);

public interface IMongoRawSeriesReader
{
    Task<IReadOnlyList<RawSeriesPoint>> ReadSeriesAsync(
        string mongoUri,
        string databaseName,
        string collectionName,
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        string paramId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken);
}
