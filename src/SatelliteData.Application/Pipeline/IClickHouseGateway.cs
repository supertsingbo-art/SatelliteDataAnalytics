namespace SatelliteData.Application.Pipeline;

using SatelliteData.Application.Tasks;

public interface IClickHouseGateway
{
    Task ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken);

    Task EnsureHqParamPointTableAsync(CancellationToken cancellationToken);

    Task InsertJsonEachRowAsync(string tableName, IReadOnlyList<string> jsonRows, CancellationToken cancellationToken);

    /// <summary>攒批写入 <c>hq_param_point</c>（累积到阈值或超时后由网关统一刷写）。</summary>
    Task InsertHqParamPointsAsync(IReadOnlyList<HqParamPointInsertRow> rows, CancellationToken cancellationToken);

    /// <summary>将网关内部尚未刷写的攒批数据强制落盘（关键屏障：删除旧版本 / 任务成功前必须调用）。</summary>
    Task FlushAsync(CancellationToken cancellationToken);

    /// <summary>查询单点最新版本（ReplacingMergeTree 按 version 降序）。</summary>
    Task<HqParamPointInsertRow?> QueryLatestPointAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        string paramId,
        DateTimeOffset ts,
        CancellationToken cancellationToken);

    /// <summary>复核后写入新版本（更新 <c>is_confirmed_outlier</c>）。</summary>
    Task InsertReviewedPointVersionsAsync(
        IReadOnlyList<HqParamPointInsertRow> rows,
        CancellationToken cancellationToken);

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

    /// <summary>离群点总数（<c>is_outlier=1</c>）。</summary>
    Task<long> CountOutlierPointsAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        IReadOnlyList<string> paramIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        string? paramIdFilter,
        CancellationToken cancellationToken);

    /// <summary>离群点分页清单（按 ts、param_id 排序）。</summary>
    Task<IReadOnlyList<HqParamPointRow>> QueryOutlierPointsPageAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        IReadOnlyList<string> paramIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        string? paramIdFilter,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>单参数在视窗内的原始点数（按 ts 去重后计数，用于曲线视图说明）。</summary>
    Task<long> CountParamPointsInWindowAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        string paramId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken);

    /// <summary>时间桶聚合曲线（确定性降采样，非随机抽样）。</summary>
    Task<IReadOnlyList<AggregatedSeriesPoint>> QueryAggregatedSeriesAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        string paramId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        int bucketSeconds,
        CancellationToken cancellationToken);

    /// <summary>视窗内离群点全量（不桶聚合），按 ts 排序并限制条数。</summary>
    Task<IReadOnlyList<HqParamPointRow>> QueryOutlierPointsForChartAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        IReadOnlyList<string> paramIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        int maxOutlierPoints,
        CancellationToken cancellationToken);

    Task DeleteByClaimsAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        IReadOnlyList<PreprocessParamClaimRequest> claims,
        ulong keepVersionFromInclusive,
        CancellationToken cancellationToken);
}

public sealed record AggregatedSeriesPoint(
    DateTimeOffset Ts,
    double MinValue,
    double MaxValue,
    double AvgValue,
    long PointCount);

public sealed record HqParamPointRow(
    string ParamId,
    DateTimeOffset Ts,
    double Value,
    bool IsOutlier,
    bool IsConfirmedOutlier = false);

public sealed record HqParamPointInsertRow(
    string TasookNo,
    string SatelliteNo,
    string TestBatchId,
    string ParamId,
    DateTimeOffset Ts,
    double? RawValue,
    double? ProcessedValue,
    byte IsOutlier,
    byte IsConfirmedOutlier,
    ulong Version);
