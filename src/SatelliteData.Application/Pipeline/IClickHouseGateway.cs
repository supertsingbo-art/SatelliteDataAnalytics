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

    Task<IReadOnlyList<HqParamPointRow>> QueryHqParamPointsAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        IReadOnlyList<string> paramIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        int maxRows,
        CancellationToken cancellationToken);

    /// <summary>按时间点总数（矩阵行数）。</summary>
    Task<long> CountDistinctTimestampsAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        IReadOnlyList<string> paramIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken);

    /// <summary>按时间点分页查询（page 从 1 开始）。</summary>
    Task<IReadOnlyList<HqParamPointRow>> QueryHqParamPointsByTimestampPageAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        IReadOnlyList<string> paramIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}

public sealed record HqParamPointRow(
    string ParamId,
    DateTimeOffset Ts,
    double Value,
    bool IsOutlier);
