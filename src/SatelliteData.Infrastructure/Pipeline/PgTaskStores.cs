using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using SatelliteData.Application.Pipeline;
using SatelliteData.Application.Tasks;
using SatelliteData.Domain.Tasks;
using SatelliteData.Infrastructure;

namespace SatelliteData.Infrastructure.Pipeline;

internal static class TaskRunDbMapper
{
    public static string ToDb(TaskJobType t) => t switch
    {
        TaskJobType.Preprocess => "PREPROCESS",
        TaskJobType.Algorithm => "ALGORITHM",
        TaskJobType.Pipeline => "PIPELINE",
        TaskJobType.Webhook => "WEBHOOK",
        _ => "PIPELINE"
    };

    public static TaskJobType JobTypeFromDb(string s) => s.ToUpperInvariant() switch
    {
        "PREPROCESS" => TaskJobType.Preprocess,
        "ALGORITHM" => TaskJobType.Algorithm,
        "PIPELINE" => TaskJobType.Pipeline,
        "WEBHOOK" => TaskJobType.Webhook,
        _ => TaskJobType.Pipeline
    };

    public static string StatusToDb(TaskRunStatus s) => s.ToString();

    public static TaskRunStatus StatusFromDb(string s) =>
        Enum.TryParse<TaskRunStatus>(s, true, out var v) ? v : TaskRunStatus.Queued;

    public static string TriggerToDb(TaskTriggerType t) => t switch
    {
        TaskTriggerType.Trial => "TRIAL",
        TaskTriggerType.Scheduled => "SCHEDULED",
        _ => "API"
    };

    public static TaskTriggerType TriggerFromDb(string s) => s.ToUpperInvariant() switch
    {
        "TRIAL" => TaskTriggerType.Trial,
        "SCHEDULED" => TaskTriggerType.Scheduled,
        _ => TaskTriggerType.Api
    };
}

public sealed class PgTaskRunRepository : ITaskRunRepository
{
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS task_run (
            run_id uuid PRIMARY KEY,
            parent_run_id uuid,
            job_id varchar(128) UNIQUE,
            job_type varchar(32) NOT NULL,
            trigger_type varchar(32) NOT NULL,
            status varchar(32) NOT NULL,
            idempotency_key varchar(128) NOT NULL,
            tasook_no varchar(64) NOT NULL,
            satellite_no varchar(64) NOT NULL,
            test_batch_id varchar(128),
            window_start timestamptz,
            window_end timestamptz,
            filter_template_id uuid,
            filter_template_version int,
            algorithm_template_id uuid,
            algorithm_template_version int,
            report_template_id uuid,
            report_template_version int,
            progress_percent numeric(5,2) NOT NULL DEFAULT 0,
            current_step varchar(128),
            start_time timestamptz,
            end_time timestamptz,
            timeout_flag boolean NOT NULL DEFAULT false,
            error_code varchar(64),
            error_msg text,
            created_by uuid,
            created_at timestamptz NOT NULL DEFAULT now(),
            UNIQUE (idempotency_key)
        );
        CREATE INDEX IF NOT EXISTS idx_task_run_status_created ON task_run(status, created_at);
        """;

    private readonly string _cs;
    private readonly ILogger<PgTaskRunRepository> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _ready;

    public PgTaskRunRepository(IOptions<DatabaseConnectionOptions> options, ILogger<PgTaskRunRepository> logger)
    {
        _cs = options.Value.Postgres;
        _logger = logger;
    }

    private async Task EnsureAsync(CancellationToken cancellationToken)
    {
        if (_ready) return;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_ready) return;
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

    public async Task InsertAsync(TaskRun run, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO task_run (
              run_id, parent_run_id, job_id, job_type, trigger_type, status, idempotency_key,
              tasook_no, satellite_no, test_batch_id, window_start, window_end,
              filter_template_id, filter_template_version, algorithm_template_id, algorithm_template_version,
              report_template_id, report_template_version, progress_percent, current_step, start_time, end_time,
              timeout_flag, error_code, error_msg, created_by, created_at
            ) VALUES (
              @run_id, @parent_run_id, @job_id, @job_type, @trigger_type, @status, @idempotency_key,
              @tasook_no, @satellite_no, @test_batch_id, @window_start, @window_end,
              @filter_template_id, @filter_template_version, @algorithm_template_id, @algorithm_template_version,
              @report_template_id, @report_template_version, @progress_percent, @current_step, @start_time, @end_time,
              @timeout_flag, @error_code, @error_msg, @created_by, @created_at
            )
            """,
            conn);
        AddParams(cmd, run);
        try
        {
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            _logger.LogDebug("Idempotency duplicate {Key}", run.IdempotencyKey);
            throw;
        }
    }

    public async Task UpdateAsync(TaskRun run, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE task_run SET
              job_type=@job_type, trigger_type=@trigger_type, status=@status,
              tasook_no=@tasook_no, satellite_no=@satellite_no, test_batch_id=@test_batch_id,
              window_start=@window_start, window_end=@window_end,
              filter_template_id=@filter_template_id, filter_template_version=@filter_template_version,
              algorithm_template_id=@algorithm_template_id, algorithm_template_version=@algorithm_template_version,
              report_template_id=@report_template_id, report_template_version=@report_template_version,
              progress_percent=@progress_percent, current_step=@current_step, start_time=@start_time, end_time=@end_time,
              timeout_flag=@timeout_flag, error_code=@error_code, error_msg=@error_msg
            WHERE run_id=@run_id
            """,
            conn);
        AddParams(cmd, run);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TaskRun?> GetByRunIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("SELECT * FROM task_run WHERE run_id=@id", conn);
        cmd.Parameters.AddWithValue("id", runId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return Read(reader);
    }

    public async Task<TaskRun?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("SELECT * FROM task_run WHERE idempotency_key=@k", conn);
        cmd.Parameters.AddWithValue("k", idempotencyKey);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return Read(reader);
    }

    public async Task<IReadOnlyList<TaskRun>> ListRecentAsync(int limit, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        var cap = Math.Clamp(limit, 1, 200);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM task_run ORDER BY created_at DESC LIMIT @lim",
            conn);
        cmd.Parameters.AddWithValue("lim", cap);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<TaskRun>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(Read(reader));
        }

        return list;
    }

    private static void AddParams(NpgsqlCommand cmd, TaskRun run)
    {
        cmd.Parameters.AddWithValue("run_id", run.RunId);
        cmd.Parameters.AddWithValue("parent_run_id", (object?)run.ParentRunId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("job_id", run.JobId);
        cmd.Parameters.AddWithValue("job_type", TaskRunDbMapper.ToDb(run.JobType));
        cmd.Parameters.AddWithValue("trigger_type", TaskRunDbMapper.TriggerToDb(run.TriggerType));
        cmd.Parameters.AddWithValue("status", TaskRunDbMapper.StatusToDb(run.Status));
        cmd.Parameters.AddWithValue("idempotency_key", run.IdempotencyKey);
        cmd.Parameters.AddWithValue("tasook_no", run.TasookNo);
        cmd.Parameters.AddWithValue("satellite_no", run.SatelliteNo);
        cmd.Parameters.AddWithValue("test_batch_id", (object?)run.TestBatchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("window_start", (object?)run.WindowStart ?? DBNull.Value);
        cmd.Parameters.AddWithValue("window_end", (object?)run.WindowEnd ?? DBNull.Value);
        cmd.Parameters.AddWithValue("filter_template_id", (object?)run.FilterTemplateId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("filter_template_version", (object?)run.FilterTemplateVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("algorithm_template_id", (object?)run.AlgorithmTemplateId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("algorithm_template_version", (object?)run.AlgorithmTemplateVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("report_template_id", (object?)run.ReportTemplateId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("report_template_version", (object?)run.ReportTemplateVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("progress_percent", run.ProgressPercent);
        cmd.Parameters.AddWithValue("current_step", (object?)run.CurrentStep ?? DBNull.Value);
        cmd.Parameters.AddWithValue("start_time", (object?)run.StartTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("end_time", (object?)run.EndTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("timeout_flag", run.TimeoutFlag);
        cmd.Parameters.AddWithValue("error_code", (object?)run.ErrorCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("error_msg", (object?)run.ErrorMsg ?? DBNull.Value);
        cmd.Parameters.AddWithValue("created_by", (object?)run.CreatedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("created_at", run.CreatedAt);
    }

    private static TaskRun Read(NpgsqlDataReader r)
    {
        static DateTimeOffset? Ts(NpgsqlDataReader x, int i) =>
            x.IsDBNull(i) ? null : x.GetFieldValue<DateTimeOffset>(i);

        return new TaskRun(
            r.GetGuid(0),
            r.IsDBNull(1) ? null : r.GetGuid(1),
            r.GetString(2),
            TaskRunDbMapper.JobTypeFromDb(r.GetString(3)),
            TaskRunDbMapper.TriggerFromDb(r.GetString(4)),
            TaskRunDbMapper.StatusFromDb(r.GetString(5)),
            r.GetString(6),
            r.GetString(7),
            r.GetString(8),
            r.IsDBNull(9) ? null : r.GetString(9),
            Ts(r, 10),
            Ts(r, 11),
            r.IsDBNull(12) ? null : r.GetGuid(12),
            r.IsDBNull(13) ? null : r.GetInt32(13),
            r.IsDBNull(14) ? null : r.GetGuid(14),
            r.IsDBNull(15) ? null : r.GetInt32(15),
            r.IsDBNull(16) ? null : r.GetGuid(16),
            r.IsDBNull(17) ? null : r.GetInt32(17),
            r.GetDecimal(18),
            r.IsDBNull(19) ? null : r.GetString(19),
            Ts(r, 20),
            Ts(r, 21),
            r.GetBoolean(22),
            r.IsDBNull(23) ? null : r.GetString(23),
            r.IsDBNull(24) ? null : r.GetString(24),
            r.IsDBNull(25) ? null : r.GetGuid(25),
            r.GetFieldValue<DateTimeOffset>(26));
    }
}

public sealed class PgTaskEventRepository : ITaskEventRepository
{
    private const string Sql = """
        CREATE TABLE IF NOT EXISTS task_event (
            event_id uuid PRIMARY KEY,
            run_id uuid NOT NULL,
            event_type varchar(64) NOT NULL,
            event_status varchar(32) NOT NULL,
            payload_json jsonb,
            error_code varchar(64),
            error_msg text,
            created_at timestamptz NOT NULL DEFAULT now()
        );
        """;

    private readonly string _cs;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _ready;

    public PgTaskEventRepository(IOptions<DatabaseConnectionOptions> options)
    {
        _cs = options.Value.Postgres;
    }

    private async Task EnsureAsync(CancellationToken cancellationToken)
    {
        if (_ready) return;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_ready) return;
            await using var conn = new NpgsqlConnection(_cs);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(Sql, conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _ready = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendAsync(TaskEvent evt, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO task_event (event_id, run_id, event_type, event_status, payload_json, error_code, error_msg, created_at)
            VALUES (@id, @run_id, @type, @status, CAST(@payload AS jsonb), @ecode, @emsg, @created)
            """,
            conn);
        cmd.Parameters.AddWithValue("id", evt.EventId);
        cmd.Parameters.AddWithValue("run_id", evt.RunId);
        cmd.Parameters.AddWithValue("type", evt.EventType);
        cmd.Parameters.AddWithValue("status", evt.EventStatus);
        cmd.Parameters.AddWithValue("payload", (object?)evt.PayloadJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ecode", (object?)evt.ErrorCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("emsg", (object?)evt.ErrorMsg ?? DBNull.Value);
        cmd.Parameters.AddWithValue("created", evt.CreatedAt);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class PgHqParamMetadataRepository : IHqParamMetadataRepository
{
    private const string Sql = """
        CREATE TABLE IF NOT EXISTS hq_param_metadata (
            metadata_id uuid PRIMARY KEY,
            run_id uuid NOT NULL,
            tasook_no varchar(64) NOT NULL,
            satellite_no varchar(64) NOT NULL,
            test_batch_id varchar(128) NOT NULL,
            param_id varchar(128) NOT NULL,
            window_start timestamptz NOT NULL,
            window_end timestamptz NOT NULL,
            filter_template_id uuid NOT NULL,
            filter_template_version int NOT NULL,
            outlier_method varchar(64),
            outlier_reason_pattern text,
            created_at timestamptz NOT NULL DEFAULT now()
        );
        """;

    private readonly string _cs;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _ready;

    public PgHqParamMetadataRepository(IOptions<DatabaseConnectionOptions> options)
    {
        _cs = options.Value.Postgres;
    }

    private async Task EnsureAsync(CancellationToken cancellationToken)
    {
        if (_ready) return;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_ready) return;
            await using var conn = new NpgsqlConnection(_cs);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(Sql, conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _ready = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task InsertAsync(HqParamMetadataRow row, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO hq_param_metadata (
              metadata_id, run_id, tasook_no, satellite_no, test_batch_id, param_id,
              window_start, window_end, filter_template_id, filter_template_version,
              outlier_method, outlier_reason_pattern, created_at
            ) VALUES (
              @id, @run_id, @t, @s, @b, @p, @ws, @we, @ft, @fv, @om, @op, now()
            )
            """,
            conn);
        cmd.Parameters.AddWithValue("id", row.MetadataId);
        cmd.Parameters.AddWithValue("run_id", row.RunId);
        cmd.Parameters.AddWithValue("t", row.TasookNo);
        cmd.Parameters.AddWithValue("s", row.SatelliteNo);
        cmd.Parameters.AddWithValue("b", row.TestBatchId);
        cmd.Parameters.AddWithValue("p", row.ParamId);
        cmd.Parameters.AddWithValue("ws", row.WindowStart);
        cmd.Parameters.AddWithValue("we", row.WindowEnd);
        cmd.Parameters.AddWithValue("ft", row.FilterTemplateId);
        cmd.Parameters.AddWithValue("fv", row.FilterTemplateVersion);
        cmd.Parameters.AddWithValue("om", (object?)row.OutlierMethod ?? DBNull.Value);
        cmd.Parameters.AddWithValue("op", (object?)row.OutlierReasonPattern ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class PgClientCallbackRepository : IClientCallbackRepository
{
    private const string Sql = """
        CREATE TABLE IF NOT EXISTS client_callbacks (
            callback_id uuid PRIMARY KEY,
            client_id uuid,
            callback_name varchar(256) NOT NULL,
            callback_url text NOT NULL,
            secret_ref varchar(256) NOT NULL,
            event_types jsonb NOT NULL DEFAULT '[]'::jsonb,
            max_retry_count int NOT NULL DEFAULT 5,
            enabled boolean NOT NULL DEFAULT true,
            created_at timestamptz NOT NULL DEFAULT now()
        );
        CREATE TABLE IF NOT EXISTS callback_deliveries (
            delivery_id uuid PRIMARY KEY,
            event_id varchar(128) NOT NULL UNIQUE,
            callback_id uuid NOT NULL REFERENCES client_callbacks(callback_id),
            run_id uuid,
            event_type varchar(64) NOT NULL,
            payload_json jsonb NOT NULL,
            status varchar(32) NOT NULL,
            retry_count int NOT NULL DEFAULT 0,
            next_retry_at timestamptz,
            response_status int,
            response_body text,
            created_at timestamptz NOT NULL DEFAULT now(),
            updated_at timestamptz NOT NULL DEFAULT now()
        );
        """;

    private readonly string _cs;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _ready;

    public PgClientCallbackRepository(IOptions<DatabaseConnectionOptions> options)
    {
        _cs = options.Value.Postgres;
    }

    private async Task EnsureAsync(CancellationToken cancellationToken)
    {
        if (_ready) return;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_ready) return;
            await using var conn = new NpgsqlConnection(_cs);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(Sql, conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _ready = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ClientCallbackRow>> GetEnabledCallbacksAsync(CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            "SELECT callback_id, callback_url, secret_ref, max_retry_count, enabled FROM client_callbacks WHERE enabled = true",
            conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<ClientCallbackRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new ClientCallbackRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetBoolean(4)));
        }

        return list;
    }

    public async Task InsertDeliveryAsync(
        Guid deliveryId,
        string eventId,
        Guid callbackId,
        Guid? runId,
        string eventType,
        string payloadJson,
        string status,
        CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO callback_deliveries (
              delivery_id, event_id, callback_id, run_id, event_type, payload_json, status, created_at, updated_at
            ) VALUES (
              @id, @eid, @cid, @run_id, @etype, CAST(@payload AS jsonb), @status, now(), now()
            )
            """,
            conn);
        cmd.Parameters.AddWithValue("id", deliveryId);
        cmd.Parameters.AddWithValue("eid", eventId);
        cmd.Parameters.AddWithValue("cid", callbackId);
        cmd.Parameters.AddWithValue("run_id", (object?)runId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("etype", eventType);
        cmd.Parameters.AddWithValue("payload", payloadJson);
        cmd.Parameters.AddWithValue("status", status);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateDeliveryAsync(
        Guid deliveryId,
        string status,
        int responseStatus,
        string? responseBody,
        CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE callback_deliveries SET status=@s, response_status=@rs, response_body=@rb, updated_at=now()
            WHERE delivery_id=@id
            """,
            conn);
        cmd.Parameters.AddWithValue("id", deliveryId);
        cmd.Parameters.AddWithValue("s", status);
        cmd.Parameters.AddWithValue("rs", responseStatus);
        cmd.Parameters.AddWithValue("rb", (object?)responseBody ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
