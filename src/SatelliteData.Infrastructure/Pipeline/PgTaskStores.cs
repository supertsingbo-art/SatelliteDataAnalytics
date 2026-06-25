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

    public static string ExecutionModeToDb(PreprocessExecutionMode m) => m switch
    {
        PreprocessExecutionMode.OnceScheduled => "ONCE_SCHEDULED",
        PreprocessExecutionMode.DailyInstance => "DAILY_INSTANCE",
        _ => "IMMEDIATE"
    };

    public static PreprocessExecutionMode? ExecutionModeFromDb(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return s.ToUpperInvariant() switch
        {
            "ONCE_SCHEDULED" => PreprocessExecutionMode.OnceScheduled,
            "DAILY_INSTANCE" => PreprocessExecutionMode.DailyInstance,
            "IMMEDIATE" => PreprocessExecutionMode.Immediate,
            _ => null
        };
    }
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
            test_batch_name varchar(256),
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
        CREATE INDEX IF NOT EXISTS idx_task_run_satellite ON task_run(tasook_no, satellite_no);
        DO $migrate_task_run_batch$
        BEGIN
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = current_schema()
                  AND table_name = 'task_run'
                  AND column_name = 'test_batch_id') THEN
                ALTER TABLE task_run ADD COLUMN IF NOT EXISTS test_batch_name varchar(256);
                UPDATE task_run
                SET test_batch_name = COALESCE(NULLIF(TRIM(test_phase_scenario), ''), test_batch_id)
                WHERE test_batch_name IS NULL;
                ALTER TABLE task_run DROP COLUMN IF EXISTS test_phase_scenario;
                ALTER TABLE task_run DROP COLUMN IF EXISTS test_batch_id;
            ELSIF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = current_schema()
                  AND table_name = 'task_run'
                  AND column_name = 'test_phase_scenario') THEN
                ALTER TABLE task_run ADD COLUMN IF NOT EXISTS test_batch_name varchar(256);
                UPDATE task_run
                SET test_batch_name = NULLIF(TRIM(test_phase_scenario), '')
                WHERE test_batch_name IS NULL;
                ALTER TABLE task_run DROP COLUMN IF EXISTS test_phase_scenario;
            END IF;
        END $migrate_task_run_batch$;
        ALTER TABLE task_run ADD COLUMN IF NOT EXISTS execution_mode varchar(32);
        ALTER TABLE task_run ADD COLUMN IF NOT EXISTS scheduled_at timestamptz;
        ALTER TABLE task_run ADD COLUMN IF NOT EXISTS schedule_id uuid;
        ALTER TABLE task_run ADD COLUMN IF NOT EXISTS hangfire_job_id varchar(128);
        CREATE INDEX IF NOT EXISTS idx_task_run_schedule_id ON task_run(schedule_id);
        CREATE INDEX IF NOT EXISTS idx_task_run_scheduled_at ON task_run(scheduled_at);
        CREATE TABLE IF NOT EXISTS preprocess_schedule (
            schedule_id uuid PRIMARY KEY,
            tasook_no varchar(64) NOT NULL,
            satellite_no varchar(64) NOT NULL,
            filter_template_id uuid NOT NULL,
            filter_template_version int NOT NULL,
            daily_time time NOT NULL,
            interval_days int NOT NULL DEFAULT 1,
            effective_from date NOT NULL,
            enabled boolean NOT NULL DEFAULT true,
            hangfire_recurring_id varchar(128) NOT NULL,
            last_run_id uuid,
            last_run_status varchar(32),
            last_run_end_at timestamptz,
            created_at timestamptz NOT NULL DEFAULT now(),
            updated_at timestamptz NOT NULL DEFAULT now()
        );
        CREATE INDEX IF NOT EXISTS idx_preprocess_schedule_satellite ON preprocess_schedule(tasook_no, satellite_no);
        CREATE TABLE IF NOT EXISTS preprocess_outlier_segment (
            segment_id uuid PRIMARY KEY,
            run_id uuid NOT NULL,
            tasook_no varchar(64) NOT NULL,
            satellite_no varchar(64) NOT NULL,
            param_id varchar(64) NOT NULL,
            segment_start timestamptz NOT NULL,
            segment_end timestamptz NOT NULL,
            outlier_method varchar(32),
            segment_kind varchar(16) NOT NULL DEFAULT 'AUTO',
            created_at timestamptz NOT NULL DEFAULT now()
        );
        CREATE INDEX IF NOT EXISTS idx_outlier_segment_run ON preprocess_outlier_segment(run_id);
        ALTER TABLE preprocess_schedule ADD COLUMN IF NOT EXISTS last_run_id uuid;
        ALTER TABLE preprocess_schedule ADD COLUMN IF NOT EXISTS last_run_status varchar(32);
        ALTER TABLE preprocess_schedule ADD COLUMN IF NOT EXISTS last_run_end_at timestamptz;
        ALTER TABLE task_run ADD COLUMN IF NOT EXISTS outlier_review_status varchar(32);
        ALTER TABLE task_run ADD COLUMN IF NOT EXISTS outlier_auto_count int NOT NULL DEFAULT 0;
        ALTER TABLE task_run ADD COLUMN IF NOT EXISTS outlier_pending_count int NOT NULL DEFAULT 0;
        ALTER TABLE task_run ADD COLUMN IF NOT EXISTS outlier_confirmed_count int NOT NULL DEFAULT 0;
        ALTER TABLE task_run ADD COLUMN IF NOT EXISTS outlier_jitter_count int NOT NULL DEFAULT 0;
        ALTER TABLE preprocess_outlier_segment ADD COLUMN IF NOT EXISTS segment_kind varchar(16) NOT NULL DEFAULT 'AUTO';
        CREATE TABLE IF NOT EXISTS preprocess_outlier_point_review (
            review_id uuid PRIMARY KEY,
            run_id uuid NOT NULL,
            tasook_no varchar(64) NOT NULL,
            satellite_no varchar(64) NOT NULL,
            param_id varchar(64) NOT NULL,
            ts timestamptz NOT NULL,
            auto_value float8,
            auto_outlier_method varchar(32),
            review_status varchar(32) NOT NULL DEFAULT 'PENDING',
            reviewed_at timestamptz,
            reviewed_by varchar(128),
            remark varchar(512),
            created_at timestamptz NOT NULL DEFAULT now(),
            UNIQUE (run_id, param_id, ts)
        );
        CREATE INDEX IF NOT EXISTS idx_outlier_review_run_status ON preprocess_outlier_point_review(run_id, review_status);
        CREATE INDEX IF NOT EXISTS idx_outlier_segment_run_kind ON preprocess_outlier_segment(run_id, segment_kind);
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
              tasook_no, satellite_no, test_batch_name, window_start, window_end,
              filter_template_id, filter_template_version, algorithm_template_id, algorithm_template_version,
              report_template_id, report_template_version, progress_percent, current_step, start_time, end_time,
              timeout_flag, error_code, error_msg, created_by, created_at,
              execution_mode, scheduled_at, schedule_id, hangfire_job_id,
              outlier_review_status, outlier_auto_count, outlier_pending_count,
              outlier_confirmed_count, outlier_jitter_count
            ) VALUES (
              @run_id, @parent_run_id, @job_id, @job_type, @trigger_type, @status, @idempotency_key,
              @tasook_no, @satellite_no, @test_batch_name, @window_start, @window_end,
              @filter_template_id, @filter_template_version, @algorithm_template_id, @algorithm_template_version,
              @report_template_id, @report_template_version, @progress_percent, @current_step, @start_time, @end_time,
              @timeout_flag, @error_code, @error_msg, @created_by, @created_at,
              @execution_mode, @scheduled_at, @schedule_id, @hangfire_job_id,
              @outlier_review_status, @outlier_auto_count, @outlier_pending_count,
              @outlier_confirmed_count, @outlier_jitter_count
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
              tasook_no=@tasook_no, satellite_no=@satellite_no, test_batch_name=@test_batch_name,
              window_start=@window_start, window_end=@window_end,
              filter_template_id=@filter_template_id, filter_template_version=@filter_template_version,
              algorithm_template_id=@algorithm_template_id, algorithm_template_version=@algorithm_template_version,
              report_template_id=@report_template_id, report_template_version=@report_template_version,
              progress_percent=@progress_percent, current_step=@current_step, start_time=@start_time, end_time=@end_time,
              timeout_flag=@timeout_flag, error_code=@error_code, error_msg=@error_msg,
              execution_mode=@execution_mode, scheduled_at=@scheduled_at, schedule_id=@schedule_id, hangfire_job_id=@hangfire_job_id,
              outlier_review_status=@outlier_review_status, outlier_auto_count=@outlier_auto_count,
              outlier_pending_count=@outlier_pending_count, outlier_confirmed_count=@outlier_confirmed_count,
              outlier_jitter_count=@outlier_jitter_count
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

    public async Task<IReadOnlyList<TaskRun>> ListByScheduleIdAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM task_run WHERE schedule_id=@sid ORDER BY created_at DESC",
            conn);
        cmd.Parameters.AddWithValue("sid", scheduleId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<TaskRun>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(Read(reader));
        }

        return list;
    }

    public async Task<TaskRun?> GetLatestByScheduleIdAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        var list = await ListByScheduleIdAsync(scheduleId, cancellationToken).ConfigureAwait(false);
        return list.FirstOrDefault();
    }

    public async Task DeleteAsync(Guid runId, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("DELETE FROM task_run WHERE run_id=@id", conn);
        cmd.Parameters.AddWithValue("id", runId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
        cmd.Parameters.AddWithValue("test_batch_name", (object?)run.TestBatchName ?? DBNull.Value);
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
        cmd.Parameters.AddWithValue(
            "execution_mode",
            run.ExecutionMode is { } em ? TaskRunDbMapper.ExecutionModeToDb(em) : DBNull.Value);
        cmd.Parameters.AddWithValue("scheduled_at", (object?)run.ScheduledAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("schedule_id", (object?)run.ScheduleId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("hangfire_job_id", (object?)run.HangfireJobId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("outlier_review_status", (object?)run.OutlierReviewStatus ?? DBNull.Value);
        cmd.Parameters.AddWithValue("outlier_auto_count", run.OutlierAutoCount);
        cmd.Parameters.AddWithValue("outlier_pending_count", run.OutlierPendingCount);
        cmd.Parameters.AddWithValue("outlier_confirmed_count", run.OutlierConfirmedCount);
        cmd.Parameters.AddWithValue("outlier_jitter_count", run.OutlierJitterCount);
    }

    private static TaskRun Read(NpgsqlDataReader r)
    {
        static DateTimeOffset? Ts(NpgsqlDataReader x, string name) =>
            x.IsDBNull(x.GetOrdinal(name)) ? null : x.GetFieldValue<DateTimeOffset>(x.GetOrdinal(name));

        static string? Str(NpgsqlDataReader x, string name) =>
            x.IsDBNull(x.GetOrdinal(name)) ? null : x.GetString(x.GetOrdinal(name));

        static Guid? GuidN(NpgsqlDataReader x, string name) =>
            x.IsDBNull(x.GetOrdinal(name)) ? null : x.GetGuid(x.GetOrdinal(name));

        static int? IntN(NpgsqlDataReader x, string name) =>
            x.IsDBNull(x.GetOrdinal(name)) ? null : x.GetInt32(x.GetOrdinal(name));

        return new TaskRun(
            r.GetGuid(r.GetOrdinal("run_id")),
            GuidN(r, "parent_run_id"),
            r.GetString(r.GetOrdinal("job_id")),
            TaskRunDbMapper.JobTypeFromDb(r.GetString(r.GetOrdinal("job_type"))),
            TaskRunDbMapper.TriggerFromDb(r.GetString(r.GetOrdinal("trigger_type"))),
            TaskRunDbMapper.StatusFromDb(r.GetString(r.GetOrdinal("status"))),
            r.GetString(r.GetOrdinal("idempotency_key")),
            r.GetString(r.GetOrdinal("tasook_no")),
            r.GetString(r.GetOrdinal("satellite_no")),
            Str(r, "test_batch_name"),
            Ts(r, "window_start"),
            Ts(r, "window_end"),
            GuidN(r, "filter_template_id"),
            IntN(r, "filter_template_version"),
            GuidN(r, "algorithm_template_id"),
            IntN(r, "algorithm_template_version"),
            GuidN(r, "report_template_id"),
            IntN(r, "report_template_version"),
            r.GetDecimal(r.GetOrdinal("progress_percent")),
            Str(r, "current_step"),
            Ts(r, "start_time"),
            Ts(r, "end_time"),
            r.GetBoolean(r.GetOrdinal("timeout_flag")),
            Str(r, "error_code"),
            Str(r, "error_msg"),
            GuidN(r, "created_by"),
            r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
            ExecutionModeFromReader(r),
            Ts(r, "scheduled_at"),
            GuidN(r, "schedule_id"),
            Str(r, "hangfire_job_id"));
    }

    private static PreprocessExecutionMode? ExecutionModeFromReader(NpgsqlDataReader r)
    {
        var ord = r.GetOrdinal("execution_mode");
        if (r.IsDBNull(ord)) return null;
        return TaskRunDbMapper.ExecutionModeFromDb(r.GetString(ord));
    }
}

public sealed class PgPreprocessScheduleRepository : IPreprocessScheduleRepository
{
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS preprocess_schedule (
            schedule_id uuid PRIMARY KEY,
            tasook_no varchar(64) NOT NULL,
            satellite_no varchar(64) NOT NULL,
            filter_template_id uuid NOT NULL,
            filter_template_version int NOT NULL,
            daily_time time NOT NULL,
            interval_days int NOT NULL DEFAULT 1,
            effective_from date NOT NULL,
            enabled boolean NOT NULL DEFAULT true,
            hangfire_recurring_id varchar(128) NOT NULL,
            last_run_id uuid,
            last_run_status varchar(32),
            last_run_end_at timestamptz,
            created_at timestamptz NOT NULL DEFAULT now(),
            updated_at timestamptz NOT NULL DEFAULT now()
        );
        ALTER TABLE preprocess_schedule ADD COLUMN IF NOT EXISTS last_run_id uuid;
        ALTER TABLE preprocess_schedule ADD COLUMN IF NOT EXISTS last_run_status varchar(32);
        ALTER TABLE preprocess_schedule ADD COLUMN IF NOT EXISTS last_run_end_at timestamptz;
        """;

    private readonly string _cs;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _ready;

    public PgPreprocessScheduleRepository(IOptions<DatabaseConnectionOptions> options) =>
        _cs = options.Value.Postgres;

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

    public async Task InsertAsync(PreprocessSchedule schedule, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO preprocess_schedule (
              schedule_id, tasook_no, satellite_no, filter_template_id, filter_template_version,
              daily_time, interval_days, effective_from, enabled, hangfire_recurring_id,
              last_run_id, last_run_status, last_run_end_at, created_at, updated_at
            ) VALUES (
              @id, @t, @s, @ft, @fv, @dt, @iv, @ef, @en, @hf, @lrid, @lrs, @lre, @ca, @ua
            )
            """,
            conn);
        AddParams(cmd, schedule);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(PreprocessSchedule schedule, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE preprocess_schedule SET
              tasook_no=@t, satellite_no=@s, filter_template_id=@ft, filter_template_version=@fv,
              daily_time=@dt, interval_days=@iv, effective_from=@ef, enabled=@en,
              hangfire_recurring_id=@hf, last_run_id=@lrid, last_run_status=@lrs,
              last_run_end_at=@lre, updated_at=@ua
            WHERE schedule_id=@id
            """,
            conn);
        AddParams(cmd, schedule);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PreprocessSchedule?> GetByIdAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("SELECT * FROM preprocess_schedule WHERE schedule_id=@id", conn);
        cmd.Parameters.AddWithValue("id", scheduleId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return Read(reader);
    }

    public async Task<IReadOnlyList<PreprocessSchedule>> ListEnabledAsync(CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM preprocess_schedule WHERE enabled=true ORDER BY created_at DESC",
            conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<PreprocessSchedule>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(Read(reader));
        }

        return list;
    }

    private static void AddParams(NpgsqlCommand cmd, PreprocessSchedule s)
    {
        cmd.Parameters.AddWithValue("id", s.ScheduleId);
        cmd.Parameters.AddWithValue("t", s.TasookNo);
        cmd.Parameters.AddWithValue("s", s.SatelliteNo);
        cmd.Parameters.AddWithValue("ft", s.FilterTemplateId);
        cmd.Parameters.AddWithValue("fv", s.FilterTemplateVersion);
        cmd.Parameters.AddWithValue("dt", s.DailyTime.ToTimeSpan());
        cmd.Parameters.AddWithValue("iv", s.IntervalDays);
        cmd.Parameters.AddWithValue("ef", s.EffectiveFrom);
        cmd.Parameters.AddWithValue("en", s.Enabled);
        cmd.Parameters.AddWithValue("hf", s.HangfireRecurringId);
        cmd.Parameters.AddWithValue("lrid", (object?)s.LastRunId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("lrs", (object?)s.LastRunStatus ?? DBNull.Value);
        cmd.Parameters.AddWithValue("lre", (object?)s.LastRunEndAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ca", s.CreatedAt);
        cmd.Parameters.AddWithValue("ua", s.UpdatedAt);
    }

    private static PreprocessSchedule Read(NpgsqlDataReader r)
    {
        static Guid? GuidN(NpgsqlDataReader x, string name) =>
            x.IsDBNull(x.GetOrdinal(name)) ? null : x.GetGuid(x.GetOrdinal(name));

        static string? Str(NpgsqlDataReader x, string name) =>
            x.IsDBNull(x.GetOrdinal(name)) ? null : x.GetString(x.GetOrdinal(name));

        static DateTimeOffset? Ts(NpgsqlDataReader x, string name) =>
            x.IsDBNull(x.GetOrdinal(name)) ? null : x.GetFieldValue<DateTimeOffset>(x.GetOrdinal(name));

        return new PreprocessSchedule(
            r.GetGuid(r.GetOrdinal("schedule_id")),
            r.GetString(r.GetOrdinal("tasook_no")),
            r.GetString(r.GetOrdinal("satellite_no")),
            r.GetGuid(r.GetOrdinal("filter_template_id")),
            r.GetInt32(r.GetOrdinal("filter_template_version")),
            TimeOnly.FromTimeSpan(r.GetTimeSpan(r.GetOrdinal("daily_time"))),
            r.GetInt32(r.GetOrdinal("interval_days")),
            DateOnly.FromDateTime(r.GetDateTime(r.GetOrdinal("effective_from"))),
            r.GetBoolean(r.GetOrdinal("enabled")),
            r.GetString(r.GetOrdinal("hangfire_recurring_id")),
            GuidN(r, "last_run_id"),
            Str(r, "last_run_status"),
            Ts(r, "last_run_end_at"),
            r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
            r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("updated_at")));
    }
}

public sealed class PgPreprocessOutlierSegmentRepository : IPreprocessOutlierSegmentRepository
{
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS preprocess_outlier_segment (
            segment_id uuid PRIMARY KEY,
            run_id uuid NOT NULL,
            tasook_no varchar(64) NOT NULL,
            satellite_no varchar(64) NOT NULL,
            param_id varchar(64) NOT NULL,
            segment_start timestamptz NOT NULL,
            segment_end timestamptz NOT NULL,
            outlier_method varchar(32),
            created_at timestamptz NOT NULL DEFAULT now()
        );
        CREATE INDEX IF NOT EXISTS idx_outlier_segment_run ON preprocess_outlier_segment(run_id);
        ALTER TABLE preprocess_outlier_segment ADD COLUMN IF NOT EXISTS segment_kind varchar(16) NOT NULL DEFAULT 'AUTO';
        CREATE INDEX IF NOT EXISTS idx_outlier_segment_run_kind ON preprocess_outlier_segment(run_id, segment_kind);
        """;

    private readonly string _cs;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _ready;

    public PgPreprocessOutlierSegmentRepository(IOptions<DatabaseConnectionOptions> options) =>
        _cs = options.Value.Postgres;

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

    public async Task InsertBatchAsync(
        IReadOnlyList<PreprocessOutlierSegment> segments,
        CancellationToken cancellationToken)
    {
        if (segments.Count == 0) return;
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        foreach (var s in segments)
        {
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO preprocess_outlier_segment (
                  segment_id, run_id, tasook_no, satellite_no, param_id,
                  segment_start, segment_end, outlier_method, segment_kind, created_at
                ) VALUES (
                  @id, @run, @t, @sat, @p, @ss, @se, @om, @sk, @ca
                )
                """,
                conn);
            cmd.Parameters.AddWithValue("id", s.SegmentId);
            cmd.Parameters.AddWithValue("run", s.RunId);
            cmd.Parameters.AddWithValue("t", s.TasookNo);
            cmd.Parameters.AddWithValue("sat", s.SatelliteNo);
            cmd.Parameters.AddWithValue("p", s.ParamId);
            cmd.Parameters.AddWithValue("ss", s.SegmentStart);
            cmd.Parameters.AddWithValue("se", s.SegmentEnd);
            cmd.Parameters.AddWithValue("om", (object?)s.OutlierMethod ?? DBNull.Value);
            cmd.Parameters.AddWithValue("sk", s.SegmentKind);
            cmd.Parameters.AddWithValue("ca", s.CreatedAt);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<IReadOnlyList<PreprocessOutlierSegment>> ListByRunIdAsync(
        Guid runId,
        CancellationToken cancellationToken) =>
        ListInternalAsync(runId, null, cancellationToken);

    public Task<IReadOnlyList<PreprocessOutlierSegment>> ListByRunIdAndKindAsync(
        Guid runId,
        string segmentKind,
        CancellationToken cancellationToken) =>
        ListInternalAsync(runId, segmentKind, cancellationToken);

    private async Task<IReadOnlyList<PreprocessOutlierSegment>> ListInternalAsync(
        Guid runId,
        string? segmentKind,
        CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        var sql = segmentKind is null
            ? "SELECT * FROM preprocess_outlier_segment WHERE run_id=@run ORDER BY segment_start"
            : "SELECT * FROM preprocess_outlier_segment WHERE run_id=@run AND segment_kind=@kind ORDER BY segment_start";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("run", runId);
        if (segmentKind is not null)
        {
            cmd.Parameters.AddWithValue("kind", segmentKind);
        }

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<PreprocessOutlierSegment>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadSegment(reader));
        }

        return list;
    }

    private static PreprocessOutlierSegment ReadSegment(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(reader.GetOrdinal("segment_id")),
            reader.GetGuid(reader.GetOrdinal("run_id")),
            reader.GetString(reader.GetOrdinal("tasook_no")),
            reader.GetString(reader.GetOrdinal("satellite_no")),
            reader.GetString(reader.GetOrdinal("param_id")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("segment_start")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("segment_end")),
            reader.IsDBNull(reader.GetOrdinal("outlier_method"))
                ? ""
                : reader.GetString(reader.GetOrdinal("outlier_method")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            ReadSegmentKind(reader));

    private static string ReadSegmentKind(NpgsqlDataReader reader)
    {
        try
        {
            var ord = reader.GetOrdinal("segment_kind");
            return reader.IsDBNull(ord) ? OutlierSegmentKind.Auto : reader.GetString(ord);
        }
        catch (IndexOutOfRangeException)
        {
            return OutlierSegmentKind.Auto;
        }
    }

    public async Task DeleteByRunIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("DELETE FROM preprocess_outlier_segment WHERE run_id=@run", conn);
        cmd.Parameters.AddWithValue("run", runId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteByRunIdAndKindAsync(Guid runId, string segmentKind, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM preprocess_outlier_segment WHERE run_id=@run AND segment_kind=@kind",
            conn);
        cmd.Parameters.AddWithValue("run", runId);
        cmd.Parameters.AddWithValue("kind", segmentKind);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class PgPreprocessOutlierPointReviewRepository : IPreprocessOutlierPointReviewRepository
{
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS preprocess_outlier_point_review (
            review_id uuid PRIMARY KEY,
            run_id uuid NOT NULL,
            tasook_no varchar(64) NOT NULL,
            satellite_no varchar(64) NOT NULL,
            param_id varchar(64) NOT NULL,
            ts timestamptz NOT NULL,
            auto_value float8,
            auto_outlier_method varchar(32),
            review_status varchar(32) NOT NULL DEFAULT 'PENDING',
            reviewed_at timestamptz,
            reviewed_by varchar(128),
            remark varchar(512),
            created_at timestamptz NOT NULL DEFAULT now(),
            UNIQUE (run_id, param_id, ts)
        );
        CREATE INDEX IF NOT EXISTS idx_outlier_review_run_status ON preprocess_outlier_point_review(run_id, review_status);
        """;

    private readonly string _cs;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _ready;

    public PgPreprocessOutlierPointReviewRepository(IOptions<DatabaseConnectionOptions> options) =>
        _cs = options.Value.Postgres;

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

    public async Task InsertBatchAsync(
        IReadOnlyList<PreprocessOutlierPointReview> reviews,
        CancellationToken cancellationToken)
    {
        if (reviews.Count == 0) return;
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        int nn = 0;
        foreach (var r in reviews)
        {
            if (++nn == 295)
            { }
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO preprocess_outlier_point_review (
                  review_id, run_id, tasook_no, satellite_no, param_id, ts,
                  auto_value, auto_outlier_method, review_status, reviewed_at, reviewed_by, remark, created_at
                ) VALUES (
                  @id, @run, @t, @sat, @p, @ts, @av, @om, @st, @ra, @rb, @rm, @ca
                )
                """,
                conn);
            cmd.Parameters.AddWithValue("id", r.ReviewId);
            cmd.Parameters.AddWithValue("run", r.RunId);
            cmd.Parameters.AddWithValue("t", r.TasookNo);
            cmd.Parameters.AddWithValue("sat", r.SatelliteNo);
            cmd.Parameters.AddWithValue("p", r.ParamId);
            cmd.Parameters.AddWithValue("ts", r.Ts);
            cmd.Parameters.AddWithValue("av", (object?)r.AutoValue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("om", (object?)r.AutoOutlierMethod ?? DBNull.Value);
            cmd.Parameters.AddWithValue("st", r.ReviewStatus);
            cmd.Parameters.AddWithValue("ra", (object?)r.ReviewedAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("rb", (object?)r.ReviewedBy ?? DBNull.Value);
            cmd.Parameters.AddWithValue("rm", (object?)r.Remark ?? DBNull.Value);
            cmd.Parameters.AddWithValue("ca", r.CreatedAt);
            try
            {
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) 
            { }
        }
    }

    public async Task<(IReadOnlyList<PreprocessOutlierPointReview> Items, long Total)> ListPageAsync(
        Guid runId,
        string? statusFilter,
        string? paramIdFilter,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, 200);
        var offset = (safePage - 1) * safePageSize;
        var where = "run_id=@run";
        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            where += " AND review_status=@st";
        }

        if (!string.IsNullOrWhiteSpace(paramIdFilter))
        {
            where += " AND param_id=@p";
        }

        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var countCmd = new NpgsqlCommand($"SELECT count(*) FROM preprocess_outlier_point_review WHERE {where}", conn);
        AddListParams(countCmd, runId, statusFilter, paramIdFilter);
        var total = Convert.ToInt64(await countCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));

        await using var cmd = new NpgsqlCommand(
            $"""
             SELECT * FROM preprocess_outlier_point_review WHERE {where}
             ORDER BY ts ASC, param_id ASC
             LIMIT @lim OFFSET @off
             """,
            conn);
        AddListParams(cmd, runId, statusFilter, paramIdFilter);
        cmd.Parameters.AddWithValue("lim", safePageSize);
        cmd.Parameters.AddWithValue("off", offset);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<PreprocessOutlierPointReview>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadReview(reader));
        }

        return (list, total);
    }

    public async Task<IReadOnlyList<PreprocessOutlierPointReview>> ListByRunIdAndStatusAsync(
        Guid runId,
        string status,
        CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM preprocess_outlier_point_review WHERE run_id=@run AND review_status=@st ORDER BY param_id, ts",
            conn);
        cmd.Parameters.AddWithValue("run", runId);
        cmd.Parameters.AddWithValue("st", status);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<PreprocessOutlierPointReview>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadReview(reader));
        }

        return list;
    }

    public async Task<IReadOnlyList<PreprocessOutlierPointReview>> ListByRunIdAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM preprocess_outlier_point_review WHERE run_id=@run ORDER BY param_id, ts",
            conn);
        cmd.Parameters.AddWithValue("run", runId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<PreprocessOutlierPointReview>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadReview(reader));
        }

        return list;
    }

    public async Task<IReadOnlyDictionary<string, int>> CountByStatusAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            "SELECT review_status, count(*) FROM preprocess_outlier_point_review WHERE run_id=@run GROUP BY review_status",
            conn);
        cmd.Parameters.AddWithValue("run", runId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var dict = new Dictionary<string, int>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            dict[reader.GetString(0)] = (int)reader.GetInt64(1);
        }

        return dict;
    }

    public async Task<bool> UpdateStatusBatchAsync(
        Guid runId,
        IReadOnlyList<OutlierReviewUpdate> updates,
        DateTimeOffset reviewedAt,
        string? reviewedBy,
        CancellationToken cancellationToken)
    {
        if (updates.Count == 0) return true;
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        var updated = 0;
        foreach (var u in updates)
        {
            await using var cmd = new NpgsqlCommand(
                """
                UPDATE preprocess_outlier_point_review SET
                  review_status=@st, reviewed_at=@ra, reviewed_by=@rb, remark=@rm
                WHERE run_id=@run AND param_id=@p AND ts=@ts AND review_status='PENDING'
                """,
                conn);
            cmd.Parameters.AddWithValue("st", u.Status);
            cmd.Parameters.AddWithValue("ra", reviewedAt);
            cmd.Parameters.AddWithValue("rb", (object?)reviewedBy ?? DBNull.Value);
            cmd.Parameters.AddWithValue("rm", (object?)u.Remark ?? DBNull.Value);
            cmd.Parameters.AddWithValue("run", runId);
            cmd.Parameters.AddWithValue("p", u.ParamId);
            cmd.Parameters.AddWithValue("ts", u.Ts);
            updated += await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return updated > 0;
    }

    public async Task DeleteByRunIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("DELETE FROM preprocess_outlier_point_review WHERE run_id=@run", conn);
        cmd.Parameters.AddWithValue("run", runId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddListParams(
        NpgsqlCommand cmd,
        Guid runId,
        string? statusFilter,
        string? paramIdFilter)
    {
        cmd.Parameters.AddWithValue("run", runId);
        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            cmd.Parameters.AddWithValue("st", statusFilter.Trim().ToUpperInvariant());
        }

        if (!string.IsNullOrWhiteSpace(paramIdFilter))
        {
            cmd.Parameters.AddWithValue("p", paramIdFilter.Trim());
        }
    }

    private static PreprocessOutlierPointReview ReadReview(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(reader.GetOrdinal("review_id")),
            reader.GetGuid(reader.GetOrdinal("run_id")),
            reader.GetString(reader.GetOrdinal("tasook_no")),
            reader.GetString(reader.GetOrdinal("satellite_no")),
            reader.GetString(reader.GetOrdinal("param_id")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("ts")),
            reader.IsDBNull(reader.GetOrdinal("auto_value")) ? null : reader.GetDouble(reader.GetOrdinal("auto_value")),
            reader.IsDBNull(reader.GetOrdinal("auto_outlier_method"))
                ? null
                : reader.GetString(reader.GetOrdinal("auto_outlier_method")),
            reader.GetString(reader.GetOrdinal("review_status")),
            reader.IsDBNull(reader.GetOrdinal("reviewed_at"))
                ? null
                : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("reviewed_at")),
            reader.IsDBNull(reader.GetOrdinal("reviewed_by"))
                ? null
                : reader.GetString(reader.GetOrdinal("reviewed_by")),
            reader.IsDBNull(reader.GetOrdinal("remark")) ? null : reader.GetString(reader.GetOrdinal("remark")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")));
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

    public async Task DeleteByRunIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("DELETE FROM task_event WHERE run_id=@id", conn);
        cmd.Parameters.AddWithValue("id", runId);
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

    public async Task<IReadOnlyList<HqParamMetadataRow>> ListByRunIdAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM hq_param_metadata WHERE run_id=@run ORDER BY param_id",
            conn);
        cmd.Parameters.AddWithValue("run", runId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<HqParamMetadataRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadMetadata(reader));
        }

        return list;
    }

    public async Task DeleteByRunIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("DELETE FROM hq_param_metadata WHERE run_id=@run", conn);
        cmd.Parameters.AddWithValue("run", runId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static HqParamMetadataRow ReadMetadata(NpgsqlDataReader r) =>
        new(
            r.GetGuid(r.GetOrdinal("metadata_id")),
            r.GetGuid(r.GetOrdinal("run_id")),
            r.GetString(r.GetOrdinal("tasook_no")),
            r.GetString(r.GetOrdinal("satellite_no")),
            r.GetString(r.GetOrdinal("test_batch_id")),
            r.GetString(r.GetOrdinal("param_id")),
            r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("window_start")),
            r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("window_end")),
            r.GetGuid(r.GetOrdinal("filter_template_id")),
            r.GetInt32(r.GetOrdinal("filter_template_version")),
            r.IsDBNull(r.GetOrdinal("outlier_method")) ? null : r.GetString(r.GetOrdinal("outlier_method")),
            r.IsDBNull(r.GetOrdinal("outlier_reason_pattern"))
                ? null
                : r.GetString(r.GetOrdinal("outlier_reason_pattern")));
}

public sealed class PgPreprocessParamClaimRepository : IPreprocessParamClaimRepository
{
    private const string ActiveStatus = "ACTIVE";
    private const string CommittedStatus = "COMMITTED";
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS preprocess_param_claim (
            claim_id uuid PRIMARY KEY,
            run_id uuid NOT NULL,
            tasook_no varchar(64) NOT NULL,
            satellite_no varchar(64) NOT NULL,
            param_id varchar(128) NOT NULL,
            segment_start timestamptz NOT NULL,
            segment_end timestamptz NOT NULL,
            filter_template_id uuid NOT NULL,
            filter_template_version int NOT NULL,
            status varchar(16) NOT NULL,
            created_at timestamptz NOT NULL DEFAULT now()
        );
        CREATE INDEX IF NOT EXISTS idx_preprocess_param_claim_run
            ON preprocess_param_claim(run_id, status);
        CREATE INDEX IF NOT EXISTS idx_preprocess_param_claim_lookup
            ON preprocess_param_claim(tasook_no, satellite_no, param_id, segment_start, segment_end, status);
        CREATE OR REPLACE FUNCTION enforce_preprocess_param_claim_no_overlap()
        RETURNS trigger AS $$
        DECLARE
            key_hash bigint;
        BEGIN
            IF NEW.segment_start >= NEW.segment_end THEN
                RAISE EXCEPTION 'segment_start must be before segment_end';
            END IF;

            key_hash := hashtextextended(
                COALESCE(NEW.tasook_no, '') || '|' || COALESCE(NEW.satellite_no, '') || '|' || COALESCE(NEW.param_id, ''),
                0);
            PERFORM pg_advisory_xact_lock(key_hash);

            IF EXISTS (
                SELECT 1
                FROM preprocess_param_claim c
                WHERE c.run_id <> NEW.run_id
                  AND c.tasook_no = NEW.tasook_no
                  AND c.satellite_no = NEW.satellite_no
                  AND c.param_id = NEW.param_id
                  AND c.status IN ('ACTIVE', 'COMMITTED')
                  AND tstzrange(c.segment_start, c.segment_end, '[)')
                      && tstzrange(NEW.segment_start, NEW.segment_end, '[)')
            ) THEN
                RAISE EXCEPTION 'preprocess param claim overlap for %', NEW.param_id USING ERRCODE = '23505';
            END IF;

            RETURN NEW;
        END;
        $$ LANGUAGE plpgsql;
        DROP TRIGGER IF EXISTS trg_preprocess_param_claim_no_overlap ON preprocess_param_claim;
        CREATE TRIGGER trg_preprocess_param_claim_no_overlap
            BEFORE INSERT OR UPDATE OF tasook_no, satellite_no, param_id, segment_start, segment_end, status
            ON preprocess_param_claim
            FOR EACH ROW
            EXECUTE FUNCTION enforce_preprocess_param_claim_no_overlap();
        """;

    private readonly string _cs;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _ready;

    public PgPreprocessParamClaimRepository(IOptions<DatabaseConnectionOptions> options) =>
        _cs = options.Value.Postgres;

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

    public async Task<PreprocessParamClaimAcquireResult> TryAcquireAsync(
        Guid runId,
        string tasookNo,
        string satelliteNo,
        Guid filterTemplateId,
        int filterTemplateVersion,
        IReadOnlyList<PreprocessParamClaimRequest> claims,
        CancellationToken cancellationToken)
    {
        var normalized = claims
            .Where(c => !string.IsNullOrWhiteSpace(c.ParamId) && c.SegmentStart < c.SegmentEnd)
            .Select(c => c with { ParamId = c.ParamId.Trim() })
            .OrderBy(c => c.ParamId, StringComparer.Ordinal)
            .ThenBy(c => c.SegmentStart)
            .ToArray();
        if (normalized.Length == 0)
        {
            return PreprocessParamClaimAcquireResult.Success;
        }

        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            foreach (var claim in normalized)
            {
                await using var cmd = new NpgsqlCommand(
                    """
                    INSERT INTO preprocess_param_claim (
                      claim_id, run_id, tasook_no, satellite_no, param_id,
                      segment_start, segment_end, filter_template_id, filter_template_version, status, created_at
                    ) VALUES (
                      @id, @run_id, @tasook_no, @satellite_no, @param_id,
                      @segment_start, @segment_end, @template_id, @template_version, @status, now()
                    )
                    """,
                    conn,
                    tx);
                cmd.Parameters.AddWithValue("id", Guid.NewGuid());
                cmd.Parameters.AddWithValue("run_id", runId);
                cmd.Parameters.AddWithValue("tasook_no", tasookNo);
                cmd.Parameters.AddWithValue("satellite_no", satelliteNo);
                cmd.Parameters.AddWithValue("param_id", claim.ParamId);
                cmd.Parameters.AddWithValue("segment_start", claim.SegmentStart);
                cmd.Parameters.AddWithValue("segment_end", claim.SegmentEnd);
                cmd.Parameters.AddWithValue("template_id", filterTemplateId);
                cmd.Parameters.AddWithValue("template_version", filterTemplateVersion);
                cmd.Parameters.AddWithValue("status", ActiveStatus);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return PreprocessParamClaimAcquireResult.Success;
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return await QueryConflictsAsync(
                conn,
                runId,
                tasookNo,
                satelliteNo,
                normalized,
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task MarkCommittedByRunIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE preprocess_param_claim
            SET status = @committed
            WHERE run_id = @run_id AND status = @active
            """,
            conn);
        cmd.Parameters.AddWithValue("run_id", runId);
        cmd.Parameters.AddWithValue("active", ActiveStatus);
        cmd.Parameters.AddWithValue("committed", CommittedStatus);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReleaseActiveByRunIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM preprocess_param_claim WHERE run_id = @run_id AND status = @active",
            conn);
        cmd.Parameters.AddWithValue("run_id", runId);
        cmd.Parameters.AddWithValue("active", ActiveStatus);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteByRunIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM preprocess_param_claim WHERE run_id = @run_id",
            conn);
        cmd.Parameters.AddWithValue("run_id", runId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<PreprocessParamClaimAcquireResult> QueryConflictsAsync(
        NpgsqlConnection conn,
        Guid runId,
        string tasookNo,
        string satelliteNo,
        IReadOnlyList<PreprocessParamClaimRequest> claims,
        CancellationToken cancellationToken)
    {
        var conflictParams = new HashSet<string>(StringComparer.Ordinal);
        PreprocessParamClaimConflict? first = null;
        foreach (var claim in claims)
        {
            await using var cmd = new NpgsqlCommand(
                """
                SELECT param_id, run_id, filter_template_id, filter_template_version
                FROM preprocess_param_claim
                WHERE run_id <> @run_id
                  AND tasook_no = @tasook_no
                  AND satellite_no = @satellite_no
                  AND param_id = @param_id
                  AND status IN ('ACTIVE', 'COMMITTED')
                  AND tstzrange(segment_start, segment_end, '[)') && tstzrange(@segment_start, @segment_end, '[)')
                ORDER BY created_at DESC
                LIMIT 1
                """,
                conn);
            cmd.Parameters.AddWithValue("run_id", runId);
            cmd.Parameters.AddWithValue("tasook_no", tasookNo);
            cmd.Parameters.AddWithValue("satellite_no", satelliteNo);
            cmd.Parameters.AddWithValue("param_id", claim.ParamId);
            cmd.Parameters.AddWithValue("segment_start", claim.SegmentStart);
            cmd.Parameters.AddWithValue("segment_end", claim.SegmentEnd);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var paramId = reader.GetString(reader.GetOrdinal("param_id"));
            conflictParams.Add(paramId);
            first ??= new PreprocessParamClaimConflict(
                paramId,
                reader.GetGuid(reader.GetOrdinal("run_id")),
                reader.GetGuid(reader.GetOrdinal("filter_template_id")),
                reader.GetInt32(reader.GetOrdinal("filter_template_version")));
        }

        return PreprocessParamClaimAcquireResult.Conflict(
            conflictParams.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            first);
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
