namespace SatelliteData.Application.Pipeline;

public interface IClickHouseGateway
{
    Task ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken);

    Task EnsureHqParamPointTableAsync(CancellationToken cancellationToken);

    Task InsertJsonEachRowAsync(string tableName, IReadOnlyList<string> jsonRows, CancellationToken cancellationToken);

    Task<IReadOnlyList<(DateTimeOffset Ts, double Value)>> QueryProcessedSeriesAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        string paramId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken);
}
