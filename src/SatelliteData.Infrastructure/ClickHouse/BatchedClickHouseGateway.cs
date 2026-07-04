using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SatelliteData.Application.Pipeline;
using SatelliteData.Application.Tasks;

namespace SatelliteData.Infrastructure.ClickHouse;

/// <summary>
/// 攒批写入装饰器：<c>hq_param_point</c> 写入先入内存缓冲，达到行数阈值（默认 1 万行）
/// 或时间阈值（默认 1 秒，由 <see cref="ClickHouseBatchFlushHostedService"/> 触发）后统一刷写。
/// 查询 / DDL / 删除 直接委托内部 <see cref="ClickHouseHttpGateway"/>；删除前强制 Flush 以保证版本时序。
/// </summary>
public sealed class BatchedClickHouseGateway : IClickHouseGateway
{
    private readonly ClickHouseHttpGateway _inner;
    private readonly ClickHouseBulkWriter _writer;
    private readonly ILogger<BatchedClickHouseGateway> _logger;
    private readonly int _rowThreshold;
    private readonly TimeSpan _flushInterval;

    private readonly object _gate = new();
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private List<HqParamPointInsertRow> _buffer = new();
    private DateTimeOffset? _oldestBufferedAt;

    public BatchedClickHouseGateway(
        ClickHouseHttpGateway inner,
        ClickHouseBulkWriter writer,
        IOptions<PipelineOptions> options,
        ILogger<BatchedClickHouseGateway> logger)
    {
        _inner = inner;
        _writer = writer;
        _logger = logger;
        var opt = options.Value;
        _rowThreshold = Math.Max(1, opt.ClickHouseBatchRowThreshold);
        _flushInterval = TimeSpan.FromMilliseconds(Math.Max(1, opt.ClickHouseBatchFlushIntervalMs));
    }

    public TimeSpan FlushInterval => _flushInterval;

    public async Task InsertHqParamPointsAsync(
        IReadOnlyList<HqParamPointInsertRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        bool shouldFlush;
        lock (_gate)
        {
            _buffer.AddRange(rows);
            _oldestBufferedAt ??= DateTimeOffset.UtcNow;
            shouldFlush = _buffer.Count >= _rowThreshold;
        }

        if (shouldFlush)
        {
            await FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        await _flushLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<HqParamPointInsertRow> batch;
            lock (_gate)
            {
                if (_buffer.Count == 0)
                {
                    _oldestBufferedAt = null;
                    return;
                }

                batch = _buffer;
                _buffer = new List<HqParamPointInsertRow>();
                _oldestBufferedAt = null;
            }

            await _writer.WriteHqParamPointsAsync(batch, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _flushLock.Release();
        }
    }

    /// <summary>供后台定时服务调用：仅当最旧缓冲行超过时间阈值时刷写。</summary>
    public async Task FlushIfDueAsync(CancellationToken cancellationToken)
    {
        bool due;
        lock (_gate)
        {
            due = _buffer.Count > 0
                && _oldestBufferedAt.HasValue
                && DateTimeOffset.UtcNow - _oldestBufferedAt.Value >= _flushInterval;
        }

        if (due)
        {
            await FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task InsertReviewedPointVersionsAsync(
        IReadOnlyList<HqParamPointInsertRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        // 复核写入量小且需近实时可见，直接同步落盘，不进入攒批缓冲。
        await _writer.WriteHqParamPointsAsync(rows, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteByClaimsAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        IReadOnlyList<PreprocessParamClaimRequest> claims,
        ulong keepVersionFromInclusive,
        CancellationToken cancellationToken)
    {
        // 删除旧版本前必须确保本次写入的新版本已落盘，否则可能删掉尚未刷写的可见数据。
        await FlushAsync(cancellationToken).ConfigureAwait(false);
        await _inner.DeleteByClaimsAsync(
            tasookNo,
            satelliteNo,
            testBatchId,
            claims,
            keepVersionFromInclusive,
            cancellationToken).ConfigureAwait(false);
    }

    public Task InsertJsonEachRowAsync(
        string tableName,
        IReadOnlyList<string> jsonRows,
        CancellationToken cancellationToken) =>
        _inner.InsertJsonEachRowAsync(tableName, jsonRows, cancellationToken);

    public Task ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken) =>
        _inner.ExecuteNonQueryAsync(sql, cancellationToken);

    public Task EnsureHqParamPointTableAsync(CancellationToken cancellationToken) =>
        _inner.EnsureHqParamPointTableAsync(cancellationToken);

    public Task<HqParamPointInsertRow?> QueryLatestPointAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        string paramId,
        DateTimeOffset ts,
        CancellationToken cancellationToken) =>
        _inner.QueryLatestPointAsync(tasookNo, satelliteNo, testBatchId, paramId, ts, cancellationToken);

    public Task<IReadOnlyList<(DateTimeOffset Ts, double Value)>> QueryProcessedSeriesAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        string paramId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken) =>
        _inner.QueryProcessedSeriesAsync(
            tasookNo, satelliteNo, testBatchId, paramId, windowStart, windowEnd, cancellationToken);

    public Task<IReadOnlyList<HqParamPointRow>> QueryHqParamPointsAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        IReadOnlyList<string> paramIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        int maxRows,
        CancellationToken cancellationToken) =>
        _inner.QueryHqParamPointsAsync(
            tasookNo, satelliteNo, testBatchId, paramIds, windowStart, windowEnd, maxRows, cancellationToken);

    public Task<long> CountDistinctTimestampsAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        IReadOnlyList<string> paramIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken) =>
        _inner.CountDistinctTimestampsAsync(
            tasookNo, satelliteNo, testBatchId, paramIds, windowStart, windowEnd, cancellationToken);

    public Task<IReadOnlyList<HqParamPointRow>> QueryHqParamPointsByTimestampPageAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        IReadOnlyList<string> paramIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        int page,
        int pageSize,
        CancellationToken cancellationToken) =>
        _inner.QueryHqParamPointsByTimestampPageAsync(
            tasookNo, satelliteNo, testBatchId, paramIds, windowStart, windowEnd, page, pageSize, cancellationToken);

    public Task<long> CountOutlierPointsAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        IReadOnlyList<string> paramIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        string? paramIdFilter,
        CancellationToken cancellationToken) =>
        _inner.CountOutlierPointsAsync(
            tasookNo, satelliteNo, testBatchId, paramIds, windowStart, windowEnd, paramIdFilter, cancellationToken);

    public Task<IReadOnlyList<HqParamPointRow>> QueryOutlierPointsPageAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        IReadOnlyList<string> paramIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        string? paramIdFilter,
        int page,
        int pageSize,
        CancellationToken cancellationToken) =>
        _inner.QueryOutlierPointsPageAsync(
            tasookNo, satelliteNo, testBatchId, paramIds, windowStart, windowEnd, paramIdFilter, page, pageSize, cancellationToken);

    public Task<long> CountParamPointsInWindowAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        string paramId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken) =>
        _inner.CountParamPointsInWindowAsync(
            tasookNo, satelliteNo, testBatchId, paramId, windowStart, windowEnd, cancellationToken);

    public Task<IReadOnlyList<AggregatedSeriesPoint>> QueryAggregatedSeriesAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        string paramId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        int bucketSeconds,
        CancellationToken cancellationToken) =>
        _inner.QueryAggregatedSeriesAsync(
            tasookNo, satelliteNo, testBatchId, paramId, windowStart, windowEnd, bucketSeconds, cancellationToken);

    public Task<IReadOnlyList<HqParamPointRow>> QueryOutlierPointsForChartAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        IReadOnlyList<string> paramIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        int maxOutlierPoints,
        CancellationToken cancellationToken) =>
        _inner.QueryOutlierPointsForChartAsync(
            tasookNo, satelliteNo, testBatchId, paramIds, windowStart, windowEnd, maxOutlierPoints, cancellationToken);
}
