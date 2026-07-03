using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using SatelliteData.Application.Tasks;
using SatelliteData.Domain.Tasks;
using SatelliteData.Infrastructure.PostgreSql;

namespace SatelliteData.Infrastructure.Pipeline;

public sealed class InMemoryOutlierMarkConfigRepository : IOutlierMarkConfigRepository
{
    private readonly object _gate = new();
    private List<OutlierMarkOption> _items = OutlierMarkConfigService.DefaultOptions().ToList();

    public Task<IReadOnlyList<OutlierMarkOption>> ListAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<OutlierMarkOption>>(_items.ToArray());
        }
    }

    public Task ReplaceAllAsync(IReadOnlyList<OutlierMarkOption> options, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            _items = options
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.MarkCode, StringComparer.Ordinal)
                .ToList();
            return Task.CompletedTask;
        }
    }
}

public sealed class PgOutlierMarkConfigRepository : IOutlierMarkConfigRepository
{
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS outlier_mark_config (
            mark_code varchar(64) PRIMARY KEY,
            mark_label varchar(128) NOT NULL,
            is_outlier boolean NOT NULL,
            sort_order int NOT NULL DEFAULT 0,
            enabled boolean NOT NULL DEFAULT true,
            updated_at timestamptz NOT NULL DEFAULT now()
        );
        """;

    private readonly string _cs;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _ready;

    public PgOutlierMarkConfigRepository(IOptions<DatabaseConnectionOptions> options) =>
        _cs = options.Value.Postgres;

    public async Task<IReadOnlyList<OutlierMarkOption>> ListAsync(CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT mark_code, mark_label, is_outlier, sort_order, enabled
            FROM outlier_mark_config
            ORDER BY sort_order, mark_code
            """,
            conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<OutlierMarkOption>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new OutlierMarkOption(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetBoolean(2),
                reader.GetInt32(3),
                reader.GetBoolean(4)));
        }

        return list.Count == 0 ? OutlierMarkConfigService.DefaultOptions() : list;
    }

    public async Task ReplaceAllAsync(IReadOnlyList<OutlierMarkOption> options, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var deleteCmd = new NpgsqlCommand("DELETE FROM outlier_mark_config", conn, tx))
        {
            await deleteCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (options.Count > 0)
        {
            const string sql = """
                INSERT INTO outlier_mark_config (
                    mark_code, mark_label, is_outlier, sort_order, enabled, updated_at
                ) VALUES (
                    @code, @label, @is_outlier, @sort_order, @enabled, @updated_at
                )
                """;
            var binders = options.Select<OutlierMarkOption, Action<NpgsqlBatchCommand>>(item => cmd =>
            {
                cmd.Parameters.AddWithValue("code", item.MarkCode);
                cmd.Parameters.AddWithValue("label", item.MarkLabel);
                cmd.Parameters.AddWithValue("is_outlier", item.IsOutlier);
                cmd.Parameters.AddWithValue("sort_order", item.SortOrder);
                cmd.Parameters.AddWithValue("enabled", item.Enabled);
                cmd.Parameters.AddWithValue("updated_at", DateTimeOffset.UtcNow);
            }).ToArray();
            await PgBatchExecutor.ExecuteBatchAsync(conn, sql, binders, cancellationToken, tx).ConfigureAwait(false);
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureAsync(CancellationToken cancellationToken)
    {
        if (_ready) return;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_ready) return;
            await PgDatabaseBootstrap.EnsureDatabaseExistsAsync(_cs, NullLogger.Instance, cancellationToken)
                .ConfigureAwait(false);
            await using var conn = new NpgsqlConnection(_cs);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(SchemaSql, conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _ready = true;
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed class InMemoryPreprocessValidRangeRepository : IPreprocessValidRangeRepository
{
    private readonly object _gate = new();
    private readonly List<PreprocessValidRange> _ranges = [];

    public Task InsertBatchAsync(IReadOnlyList<PreprocessValidRange> ranges, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            _ranges.AddRange(ranges);
            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<PreprocessValidRange>> ListByRunIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            var arr = _ranges
                .Where(x => x.RunId == runId)
                .OrderBy(x => x.RangeStart)
                .ToArray();
            return Task.FromResult<IReadOnlyList<PreprocessValidRange>>(arr);
        }
    }

    public Task DeleteByRunIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            _ranges.RemoveAll(x => x.RunId == runId);
            return Task.CompletedTask;
        }
    }
}

public sealed class PgPreprocessValidRangeRepository : IPreprocessValidRangeRepository
{
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS preprocess_valid_range (
            range_id uuid PRIMARY KEY,
            run_id uuid NOT NULL,
            tasook_no varchar(64) NOT NULL,
            satellite_no varchar(64) NOT NULL,
            range_start timestamptz NOT NULL,
            range_end timestamptz NOT NULL,
            created_at timestamptz NOT NULL DEFAULT now()
        );
        CREATE INDEX IF NOT EXISTS idx_preprocess_valid_range_run ON preprocess_valid_range(run_id, range_start);
        """;

    private readonly string _cs;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _ready;

    public PgPreprocessValidRangeRepository(IOptions<DatabaseConnectionOptions> options) =>
        _cs = options.Value.Postgres;

    public async Task InsertBatchAsync(IReadOnlyList<PreprocessValidRange> ranges, CancellationToken cancellationToken)
    {
        if (ranges.Count == 0) return;
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        const string sql = """
            INSERT INTO preprocess_valid_range (
                range_id, run_id, tasook_no, satellite_no, range_start, range_end, created_at
            ) VALUES (
                @id, @run, @t, @sat, @rs, @re, @ca
            )
            """;
        var binders = ranges.Select<PreprocessValidRange, Action<NpgsqlBatchCommand>>(r => cmd =>
        {
            cmd.Parameters.AddWithValue("id", r.RangeId);
            cmd.Parameters.AddWithValue("run", r.RunId);
            cmd.Parameters.AddWithValue("t", r.TasookNo);
            cmd.Parameters.AddWithValue("sat", r.SatelliteNo);
            cmd.Parameters.AddWithValue("rs", PgTimestamptz.Utc(r.RangeStart));
            cmd.Parameters.AddWithValue("re", PgTimestamptz.Utc(r.RangeEnd));
            cmd.Parameters.AddWithValue("ca", PgTimestamptz.Utc(r.CreatedAt));
        }).ToArray();
        await PgBatchExecutor.ExecuteBatchAsync(conn, sql, binders, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PreprocessValidRange>> ListByRunIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT range_id, run_id, tasook_no, satellite_no, range_start, range_end, created_at
            FROM preprocess_valid_range
            WHERE run_id = @run
            ORDER BY range_start
            """,
            conn);
        cmd.Parameters.AddWithValue("run", runId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<PreprocessValidRange>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new PreprocessValidRange(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetFieldValue<DateTimeOffset>(6)));
        }

        return list;
    }

    public async Task DeleteByRunIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("DELETE FROM preprocess_valid_range WHERE run_id=@run", conn);
        cmd.Parameters.AddWithValue("run", runId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureAsync(CancellationToken cancellationToken)
    {
        if (_ready) return;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_ready) return;
            await PgDatabaseBootstrap.EnsureDatabaseExistsAsync(_cs, NullLogger.Instance, cancellationToken)
                .ConfigureAwait(false);
            await using var conn = new NpgsqlConnection(_cs);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(SchemaSql, conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _ready = true;
        }
        finally
        {
            _gate.Release();
        }
    }
}
