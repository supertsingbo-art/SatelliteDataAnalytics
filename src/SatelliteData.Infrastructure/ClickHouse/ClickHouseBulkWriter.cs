using ClickHouse.Client.ADO;
using ClickHouse.Client.Copy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SatelliteData.Application.Pipeline;

namespace SatelliteData.Infrastructure.ClickHouse;

/// <summary>基于 ClickHouse.Client 二进制协议的高吞吐批量写入器（BulkCopy）。</summary>
public sealed class ClickHouseBulkWriter
{
    private const string HqParamPointTable = "hq_param_point";

    private static readonly string[] HqParamPointColumns =
    [
        "tasook_no",
        "satellite_no",
        "test_batch_id",
        "param_id",
        "ts",
        "raw_value",
        "processed_value",
        "is_outlier",
        "is_confirmed_outlier",
        "version"
    ];

    private readonly string _connectionString;
    private readonly ILogger<ClickHouseBulkWriter> _logger;

    public ClickHouseBulkWriter(
        IOptions<DatabaseConnectionOptions> options,
        ILogger<ClickHouseBulkWriter> logger)
    {
        _connectionString = options.Value.ClickHouse;
        _logger = logger;
    }

    public async Task WriteHqParamPointsAsync(
        IReadOnlyList<HqParamPointInsertRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        await using var connection = new ClickHouseConnection(_connectionString);
        using var bulkCopy = new ClickHouseBulkCopy(connection)
        {
            DestinationTableName = HqParamPointTable,
            ColumnNames = HqParamPointColumns,
            BatchSize = rows.Count
        };

        await bulkCopy.InitAsync().ConfigureAwait(false);
        await bulkCopy.WriteToServerAsync(EnumerateRows(rows), cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("ClickHouse BulkCopy 写入 {Count} 行到 {Table}", rows.Count, HqParamPointTable);
    }

    private static IEnumerable<object[]> EnumerateRows(IReadOnlyList<HqParamPointInsertRow> rows)
    {
        foreach (var r in rows)
        {
            yield return
            [
                r.TasookNo,
                r.SatelliteNo,
                r.TestBatchId,
                r.ParamId,
                r.Ts.UtcDateTime,
                (object?)r.RawValue ?? DBNull.Value,
                (object?)r.ProcessedValue ?? DBNull.Value,
                r.IsOutlier,
                r.IsConfirmedOutlier,
                r.Version
            ];
        }
    }
}
