using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using SatelliteData.Application.Assets;
using SatelliteData.Domain.Assets;

namespace SatelliteData.Infrastructure.PostgreSql;

/// <summary>
/// 将卫星 / 参数 / 指令 / 测试阶段缓存写入 PostgreSQL（<c>ConnectionStrings:Postgres</c>）。
/// 首次访问时自动 <c>CREATE TABLE IF NOT EXISTS</c> 及补充列。
/// </summary>
public sealed class PgAssetCacheRepository : IAssetCacheRepository
{
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS satellite_cache (
            tasook_no varchar(64) NOT NULL,
            satellite_no varchar(64) NOT NULL,
            satellite_name varchar(256) NOT NULL,
            satellite_type varchar(128),
            db_stage varchar(64),
            mongo_uri text,
            mongo_db_name varchar(256),
            mongo_auth_ref varchar(256),
            source_version varchar(128),
            last_synced_at timestamptz NOT NULL,
            cached_parameter_count int NOT NULL DEFAULT 0,
            cached_command_count int NOT NULL DEFAULT 0,
            is_enabled boolean NOT NULL DEFAULT true,
            raw_json jsonb NOT NULL,
            PRIMARY KEY (tasook_no, satellite_no)
        );

        CREATE TABLE IF NOT EXISTS param_cache (
            tasook_no varchar(64) NOT NULL,
            satellite_no varchar(64) NOT NULL,
            para_id int NOT NULL,
            para_code varchar(256),
            para_desc text,
            para_type_desc varchar(256),
            min_value double precision,
            max_value double precision,
            update_time int,
            proc_desc text,
            prm_sys_id int,
            source_version varchar(128),
            last_synced_at timestamptz NOT NULL,
            raw_json jsonb NOT NULL,
            PRIMARY KEY (tasook_no, satellite_no, para_id)
        );

        DO $$
        BEGIN
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = current_schema()
                  AND table_name = 'param_cache'
                  AND column_name = 'param_id')
            THEN
                UPDATE param_cache
                SET para_id = param_id::integer
                WHERE para_id IS NULL AND param_id ~ '^[0-9]+$';

                ALTER TABLE param_cache DROP CONSTRAINT IF EXISTS param_cache_pkey;
                ALTER TABLE param_cache DROP COLUMN param_id;
            END IF;
        END $$;

        ALTER TABLE param_cache DROP CONSTRAINT IF EXISTS param_cache_pkey;
        ALTER TABLE param_cache ADD COLUMN IF NOT EXISTS para_id int;
        ALTER TABLE param_cache ADD COLUMN IF NOT EXISTS para_code varchar(256);
        ALTER TABLE param_cache ADD COLUMN IF NOT EXISTS para_desc text;
        ALTER TABLE param_cache ADD COLUMN IF NOT EXISTS para_type_desc varchar(256);
        ALTER TABLE param_cache ADD COLUMN IF NOT EXISTS min_value double precision;
        ALTER TABLE param_cache ADD COLUMN IF NOT EXISTS max_value double precision;
        ALTER TABLE param_cache ADD COLUMN IF NOT EXISTS update_time int;
        ALTER TABLE param_cache ADD COLUMN IF NOT EXISTS proc_desc text;
        ALTER TABLE param_cache ADD COLUMN IF NOT EXISTS prm_sys_id int;

        UPDATE param_cache SET para_id = 0 WHERE para_id IS NULL;
        ALTER TABLE param_cache ALTER COLUMN para_id SET NOT NULL;
        ALTER TABLE param_cache DROP CONSTRAINT IF EXISTS param_cache_pkey;
        ALTER TABLE param_cache ADD PRIMARY KEY (tasook_no, satellite_no, para_id);

        CREATE TABLE IF NOT EXISTS command_cache (
            tasook_no varchar(64) NOT NULL,
            satellite_no varchar(64) NOT NULL,
            cmd_id int NOT NULL,
            cmd_code varchar(256),
            cmd_desc text,
            cmd_type int,
            cmd_len int,
            exe_time int,
            valid_flag int,
            cmd_sys_id int,
            source_version varchar(128),
            last_synced_at timestamptz NOT NULL,
            raw_json jsonb NOT NULL,
            PRIMARY KEY (tasook_no, satellite_no, cmd_id)
        );

        DO $$
        BEGIN
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = current_schema()
                  AND table_name = 'command_cache'
                  AND column_name = 'command_id')
            THEN
                UPDATE command_cache
                SET cmd_id = command_id::integer
                WHERE cmd_id IS NULL AND command_id ~ '^[0-9]+$';

                ALTER TABLE command_cache DROP CONSTRAINT IF EXISTS command_cache_pkey;
                ALTER TABLE command_cache DROP COLUMN command_id;
                ALTER TABLE command_cache DROP COLUMN IF EXISTS command_name;
            END IF;
        END $$;

        ALTER TABLE command_cache DROP CONSTRAINT IF EXISTS command_cache_pkey;
        ALTER TABLE command_cache ADD COLUMN IF NOT EXISTS cmd_id int;
        ALTER TABLE command_cache ADD COLUMN IF NOT EXISTS cmd_code varchar(256);
        ALTER TABLE command_cache ADD COLUMN IF NOT EXISTS cmd_desc text;
        ALTER TABLE command_cache ADD COLUMN IF NOT EXISTS cmd_type int;
        ALTER TABLE command_cache ADD COLUMN IF NOT EXISTS cmd_len int;
        ALTER TABLE command_cache ADD COLUMN IF NOT EXISTS exe_time int;
        ALTER TABLE command_cache ADD COLUMN IF NOT EXISTS valid_flag int;
        ALTER TABLE command_cache ADD COLUMN IF NOT EXISTS cmd_sys_id int;

        UPDATE command_cache SET cmd_id = 0 WHERE cmd_id IS NULL;
        ALTER TABLE command_cache ALTER COLUMN cmd_id SET NOT NULL;
        ALTER TABLE command_cache DROP CONSTRAINT IF EXISTS command_cache_pkey;
        ALTER TABLE command_cache ADD PRIMARY KEY (tasook_no, satellite_no, cmd_id);

        CREATE TABLE IF NOT EXISTS test_batch_cache (
            tasook_no varchar(64) NOT NULL,
            satellite_no varchar(64) NOT NULL,
            test_batch_name varchar(256) NOT NULL,
            start_ts timestamptz NOT NULL,
            end_ts timestamptz NOT NULL,
            source_version varchar(128),
            last_synced_at timestamptz NOT NULL,
            raw_json jsonb NOT NULL,
            PRIMARY KEY (tasook_no, satellite_no, test_batch_name)
        );

        DO $migrate_test_batch$
        BEGIN
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = current_schema()
                  AND table_name = 'test_batch_cache'
                  AND column_name = 'test_batch_id') THEN
                ALTER TABLE test_batch_cache ADD COLUMN IF NOT EXISTS test_batch_name varchar(256);
                UPDATE test_batch_cache
                SET test_batch_name = COALESCE(NULLIF(TRIM(scenario), ''), test_batch_id)
                WHERE test_batch_name IS NULL;
                ALTER TABLE test_batch_cache DROP COLUMN IF EXISTS scenario;
                ALTER TABLE test_batch_cache DROP CONSTRAINT IF EXISTS test_batch_cache_pkey;
                ALTER TABLE test_batch_cache DROP COLUMN IF EXISTS test_batch_id;
                ALTER TABLE test_batch_cache ALTER COLUMN test_batch_name SET NOT NULL;
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'test_batch_cache_pkey'
                      AND conrelid = 'test_batch_cache'::regclass) THEN
                    ALTER TABLE test_batch_cache
                        ADD PRIMARY KEY (tasook_no, satellite_no, test_batch_name);
                END IF;
            END IF;
        END $migrate_test_batch$;

        ALTER TABLE satellite_cache ADD COLUMN IF NOT EXISTS cached_parameter_count integer NOT NULL DEFAULT 0;
        ALTER TABLE satellite_cache ADD COLUMN IF NOT EXISTS cached_command_count integer NOT NULL DEFAULT 0;
        ALTER TABLE satellite_cache ADD COLUMN IF NOT EXISTS tasook_name varchar(256);
        ALTER TABLE satellite_cache ADD COLUMN IF NOT EXISTS is_enabled boolean NOT NULL DEFAULT true;
        """;

    private readonly string _connectionString;
    private readonly ILogger<PgAssetCacheRepository> _logger;
    private readonly SemaphoreSlim _schemaGate = new(1, 1);
    private bool _schemaReady;

    public PgAssetCacheRepository(
        IOptions<DatabaseConnectionOptions> databaseConnections,
        ILogger<PgAssetCacheRepository> logger)
    {
        _connectionString = databaseConnections.Value.Postgres;
        _logger = logger;
    }

    public async Task UpsertSatelliteAsync(SatelliteCache satellite, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        var mongoUri = satellite.MongoInfo?.MongoUri;
        var mongoDb = satellite.MongoInfo?.DbName;
        var mongoAuth = satellite.MongoInfo?.AuthRef;
        var rawJson = JsonSerializer.Serialize(satellite.RawJson);

        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO satellite_cache (
                tasook_no, tasook_name, satellite_no, satellite_name, satellite_type, db_stage,
                mongo_uri, mongo_db_name, mongo_auth_ref, source_version, last_synced_at,
                cached_parameter_count, cached_command_count, is_enabled, raw_json)
            VALUES (
                @tasook_no, @tasook_name, @satellite_no, @satellite_name, @satellite_type, @db_stage,
                @mongo_uri, @mongo_db_name, @mongo_auth_ref, @source_version, @last_synced_at,
                @cached_parameter_count, @cached_command_count, @is_enabled, @raw_json)
            ON CONFLICT (tasook_no, satellite_no) DO UPDATE SET
                tasook_name = EXCLUDED.tasook_name,
                satellite_name = EXCLUDED.satellite_name,
                satellite_type = EXCLUDED.satellite_type,
                db_stage = EXCLUDED.db_stage,
                mongo_uri = EXCLUDED.mongo_uri,
                mongo_db_name = EXCLUDED.mongo_db_name,
                mongo_auth_ref = EXCLUDED.mongo_auth_ref,
                source_version = EXCLUDED.source_version,
                last_synced_at = EXCLUDED.last_synced_at,
                cached_parameter_count = EXCLUDED.cached_parameter_count,
                cached_command_count = EXCLUDED.cached_command_count,
                raw_json = EXCLUDED.raw_json;
            """,
            conn);

        cmd.Parameters.AddWithValue("tasook_no", satellite.TasookNo);
        cmd.Parameters.AddWithValue("tasook_name", (object?)satellite.TasookName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("satellite_no", satellite.SatelliteNo);
        cmd.Parameters.AddWithValue("satellite_name", satellite.SatelliteName);
        cmd.Parameters.AddWithValue("satellite_type", (object?)satellite.SatelliteType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("db_stage", (object?)satellite.DbStage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("mongo_uri", (object?)mongoUri ?? DBNull.Value);
        cmd.Parameters.AddWithValue("mongo_db_name", (object?)mongoDb ?? DBNull.Value);
        cmd.Parameters.AddWithValue("mongo_auth_ref", (object?)mongoAuth ?? DBNull.Value);
        cmd.Parameters.AddWithValue("source_version", (object?)satellite.SourceVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("last_synced_at", satellite.LastSyncedAt);
        cmd.Parameters.AddWithValue("cached_parameter_count", satellite.CachedParameterCount);
        cmd.Parameters.AddWithValue("cached_command_count", satellite.CachedCommandCount);
        cmd.Parameters.AddWithValue("is_enabled", satellite.IsEnabled);
        var pRaw = cmd.Parameters.Add("raw_json", NpgsqlDbType.Jsonb);
        pRaw.Value = rawJson;

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertParametersAsync(IReadOnlyCollection<ParamCache> parameters, CancellationToken cancellationToken)
    {
        if (parameters.Count == 0)
        {
            return;
        }

        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var tx = await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var p in parameters)
        {
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO param_cache (
                    tasook_no, satellite_no, para_id, para_code, para_desc, para_type_desc,
                    min_value, max_value, update_time, proc_desc, prm_sys_id,
                    source_version, last_synced_at, raw_json)
                VALUES (
                    @tasook_no, @satellite_no, @para_id, @para_code, @para_desc, @para_type_desc,
                    @min_value, @max_value, @update_time, @proc_desc, @prm_sys_id,
                    @source_version, @last_synced_at, @raw_json)
                ON CONFLICT (tasook_no, satellite_no, para_id) DO UPDATE SET
                    para_code = EXCLUDED.para_code,
                    para_desc = EXCLUDED.para_desc,
                    para_type_desc = EXCLUDED.para_type_desc,
                    min_value = EXCLUDED.min_value,
                    max_value = EXCLUDED.max_value,
                    update_time = EXCLUDED.update_time,
                    proc_desc = EXCLUDED.proc_desc,
                    prm_sys_id = EXCLUDED.prm_sys_id,
                    source_version = EXCLUDED.source_version,
                    last_synced_at = EXCLUDED.last_synced_at,
                    raw_json = EXCLUDED.raw_json;
                """,
                conn,
                tx);

            cmd.Parameters.AddWithValue("tasook_no", p.TasookNo);
            cmd.Parameters.AddWithValue("satellite_no", p.SatelliteNo);
            cmd.Parameters.AddWithValue("para_id", p.ParaId);
            cmd.Parameters.AddWithValue("para_code", (object?)p.ParaCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("para_desc", (object?)p.ParaDesc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("para_type_desc", (object?)p.ParaTypeDesc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("min_value", (object?)p.MinValue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("max_value", (object?)p.MaxValue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("update_time", (object?)p.UpdateTime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("proc_desc", (object?)p.ProcDesc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("prm_sys_id", (object?)p.PrmSysId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("source_version", (object?)p.SourceVersion ?? DBNull.Value);
            cmd.Parameters.AddWithValue("last_synced_at", p.LastSyncedAt);
            cmd.Parameters.Add("raw_json", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(p.RawJson);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertCommandsAsync(
        string tasookNo,
        string satelliteNo,
        IReadOnlyCollection<CommandCache> commands,
        CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var tx = await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var del = new NpgsqlCommand(
                   "DELETE FROM command_cache WHERE tasook_no = @tasook_no AND satellite_no = @satellite_no;",
                   conn,
                   tx))
        {
            del.Parameters.AddWithValue("tasook_no", tasookNo);
            del.Parameters.AddWithValue("satellite_no", satelliteNo);
            await del.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var c in commands)
        {
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO command_cache (
                    tasook_no, satellite_no, cmd_id, cmd_code, cmd_desc, cmd_type, cmd_len,
                    exe_time, valid_flag, cmd_sys_id, source_version, last_synced_at, raw_json)
                VALUES (
                    @tasook_no, @satellite_no, @cmd_id, @cmd_code, @cmd_desc, @cmd_type, @cmd_len,
                    @exe_time, @valid_flag, @cmd_sys_id, @source_version, @last_synced_at, @raw_json)
                ON CONFLICT (tasook_no, satellite_no, cmd_id) DO UPDATE SET
                    cmd_code = EXCLUDED.cmd_code,
                    cmd_desc = EXCLUDED.cmd_desc,
                    cmd_type = EXCLUDED.cmd_type,
                    cmd_len = EXCLUDED.cmd_len,
                    exe_time = EXCLUDED.exe_time,
                    valid_flag = EXCLUDED.valid_flag,
                    cmd_sys_id = EXCLUDED.cmd_sys_id,
                    source_version = EXCLUDED.source_version,
                    last_synced_at = EXCLUDED.last_synced_at,
                    raw_json = EXCLUDED.raw_json;
                """,
                conn,
                tx);

            cmd.Parameters.AddWithValue("tasook_no", c.TasookNo);
            cmd.Parameters.AddWithValue("satellite_no", c.SatelliteNo);
            cmd.Parameters.AddWithValue("cmd_id", c.CmdId);
            cmd.Parameters.AddWithValue("cmd_code", (object?)c.CmdCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("cmd_desc", (object?)c.CmdDesc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("cmd_type", (object?)c.CmdType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("cmd_len", (object?)c.CmdLen ?? DBNull.Value);
            cmd.Parameters.AddWithValue("exe_time", (object?)c.ExeTime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("valid_flag", (object?)c.ValidFlag ?? DBNull.Value);
            cmd.Parameters.AddWithValue("cmd_sys_id", (object?)c.CmdSysId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("source_version", (object?)c.SourceVersion ?? DBNull.Value);
            cmd.Parameters.AddWithValue("last_synced_at", c.LastSyncedAt);
            cmd.Parameters.Add("raw_json", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(c.RawJson);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertTestBatchesAsync(IReadOnlyCollection<TestBatchCache> testBatches, CancellationToken cancellationToken)
    {
        if (testBatches.Count == 0)
        {
            return;
        }

        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var tx = await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var t in testBatches)
        {
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO test_batch_cache (
                    tasook_no, satellite_no, test_batch_name, start_ts, end_ts,
                    source_version, last_synced_at, raw_json)
                VALUES (
                    @tasook_no, @satellite_no, @test_batch_name, @start_ts, @end_ts,
                    @source_version, @last_synced_at, @raw_json)
                ON CONFLICT (tasook_no, satellite_no, test_batch_name) DO UPDATE SET
                    start_ts = EXCLUDED.start_ts,
                    end_ts = EXCLUDED.end_ts,
                    source_version = EXCLUDED.source_version,
                    last_synced_at = EXCLUDED.last_synced_at,
                    raw_json = EXCLUDED.raw_json;
                """,
                conn,
                tx);

            cmd.Parameters.AddWithValue("tasook_no", t.TasookNo);
            cmd.Parameters.AddWithValue("satellite_no", t.SatelliteNo);
            cmd.Parameters.AddWithValue("test_batch_name", t.TestBatchName);
            cmd.Parameters.AddWithValue("start_ts", t.StartTs);
            cmd.Parameters.AddWithValue("end_ts", t.EndTs);
            cmd.Parameters.AddWithValue("source_version", (object?)t.SourceVersion ?? DBNull.Value);
            cmd.Parameters.AddWithValue("last_synced_at", t.LastSyncedAt);
            cmd.Parameters.Add("raw_json", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(t.RawJson);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<SatelliteCache>> GetSatellitesAsync(CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT tasook_no, tasook_name, satellite_no, satellite_name, satellite_type, db_stage,
                   mongo_uri, mongo_db_name, mongo_auth_ref, source_version, last_synced_at,
                   cached_parameter_count, cached_command_count, is_enabled, raw_json::text
            FROM satellite_cache
            ORDER BY tasook_no, satellite_no;
            """,
            conn);

        var list = new List<SatelliteCache>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadSatellite(reader));
        }

        return list;
    }

    public async Task SetSatelliteEnabledAsync(
        string tasookNo,
        string satelliteNo,
        bool isEnabled,
        CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = new NpgsqlCommand(
            """
            UPDATE satellite_cache
            SET is_enabled = @is_enabled
            WHERE tasook_no = @tasook_no AND satellite_no = @satellite_no;
            """,
            conn);
        cmd.Parameters.AddWithValue("tasook_no", tasookNo);
        cmd.Parameters.AddWithValue("satellite_no", satelliteNo);
        cmd.Parameters.AddWithValue("is_enabled", isEnabled);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<SatelliteCache?> GetSatelliteAsync(string tasookNo, string satelliteNo, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT tasook_no, tasook_name, satellite_no, satellite_name, satellite_type, db_stage,
                   mongo_uri, mongo_db_name, mongo_auth_ref, source_version, last_synced_at,
                   cached_parameter_count, cached_command_count, is_enabled, raw_json::text
            FROM satellite_cache
            WHERE tasook_no = @tasook_no AND satellite_no = @satellite_no;
            """,
            conn);
        cmd.Parameters.AddWithValue("tasook_no", tasookNo);
        cmd.Parameters.AddWithValue("satellite_no", satelliteNo);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadSatellite(reader);
    }

    public async Task<IReadOnlyCollection<ParamCache>> GetParametersAsync(string tasookNo, string satelliteNo, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT tasook_no, satellite_no, para_id, para_code, para_desc, para_type_desc,
                   min_value, max_value, update_time, proc_desc, prm_sys_id,
                   source_version, last_synced_at, raw_json::text
            FROM param_cache
            WHERE tasook_no = @tasook_no AND satellite_no = @satellite_no
            ORDER BY para_id;
            """,
            conn);
        cmd.Parameters.AddWithValue("tasook_no", tasookNo);
        cmd.Parameters.AddWithValue("satellite_no", satelliteNo);

        var list = new List<ParamCache>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadParam(reader));
        }

        return list;
    }

    public async Task<IReadOnlyCollection<CommandCache>> GetCommandsAsync(string tasookNo, string satelliteNo, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT tasook_no, satellite_no, cmd_id, cmd_code, cmd_desc, cmd_type, cmd_len,
                   exe_time, valid_flag, cmd_sys_id, source_version, last_synced_at, raw_json::text
            FROM command_cache
            WHERE tasook_no = @tasook_no AND satellite_no = @satellite_no
            ORDER BY cmd_id;
            """,
            conn);
        cmd.Parameters.AddWithValue("tasook_no", tasookNo);
        cmd.Parameters.AddWithValue("satellite_no", satelliteNo);

        var list = new List<CommandCache>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadCommand(reader));
        }

        return list;
    }

    public async Task<IReadOnlyCollection<TestBatchCache>> GetTestBatchesAsync(string tasookNo, string satelliteNo, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT tasook_no, satellite_no, test_batch_name, start_ts, end_ts,
                   source_version, last_synced_at, raw_json::text
            FROM test_batch_cache
            WHERE tasook_no = @tasook_no AND satellite_no = @satellite_no
            ORDER BY start_ts DESC;
            """,
            conn);
        cmd.Parameters.AddWithValue("tasook_no", tasookNo);
        cmd.Parameters.AddWithValue("satellite_no", satelliteNo);

        var list = new List<TestBatchCache>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadTestBatch(reader));
        }

        return list;
    }

    public async Task<IReadOnlyDictionary<(string TasookNo, string SatelliteNo), IReadOnlyList<string>>>
        GetDevelopmentPhaseLabelsBySatelliteAsync(CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT tasook_no, satellite_no, test_batch_name, start_ts
            FROM test_batch_cache
            ORDER BY tasook_no, satellite_no, start_ts DESC;
            """,
            conn);

        var grouped = new Dictionary<(string, string), List<(string Label, DateTimeOffset StartTs)>>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var tasook = reader.GetString(0);
            var sat = reader.GetString(1);
            var batchName = reader.GetString(2);
            var startTs = reader.GetFieldValue<DateTimeOffset>(3);
            var label = batchName.Trim();
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            var key = (tasook, sat);
            if (!grouped.TryGetValue(key, out var list))
            {
                list = [];
                grouped[key] = list;
            }

            if (list.All(x => !string.Equals(x.Label, label, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add((label, startTs));
            }
        }

        return grouped.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value
                .OrderByDescending(x => x.StartTs)
                .Select(x => x.Label)
                .ToArray());
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = new NpgsqlCommand(
            """
            TRUNCATE TABLE command_cache, param_cache, test_batch_cache, satellite_cache;
            """,
            conn);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaReady)
        {
            return;
        }

        await _schemaGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_schemaReady)
            {
                return;
            }

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(SchemaSql, conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _schemaReady = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PostgreSQL 资产缓存表初始化失败");
            throw;
        }
        finally
        {
            _schemaGate.Release();
        }
    }

    private static SatelliteCache ReadSatellite(NpgsqlDataReader reader)
    {
        var oMongoUri = reader.GetOrdinal("mongo_uri");
        MongoConnectionInfo? mongo = null;
        if (!reader.IsDBNull(oMongoUri))
        {
            var oDb = reader.GetOrdinal("mongo_db_name");
            var oAuth = reader.GetOrdinal("mongo_auth_ref");
            mongo = new MongoConnectionInfo(
                reader.GetString(oMongoUri),
                reader.IsDBNull(oDb) ? string.Empty : reader.GetString(oDb),
                reader.IsDBNull(oAuth) ? null : reader.GetString(oAuth));
        }

        var rawText = reader.GetString(reader.GetOrdinal("raw_json"));
        var raw = ParseJson(rawText);

        var oTasookName = reader.GetOrdinal("tasook_name");
        return new SatelliteCache(
            reader.GetString(reader.GetOrdinal("tasook_no")),
            reader.IsDBNull(oTasookName) ? null : reader.GetString(oTasookName),
            reader.GetString(reader.GetOrdinal("satellite_no")),
            reader.GetString(reader.GetOrdinal("satellite_name")),
            reader.IsDBNull(reader.GetOrdinal("satellite_type")) ? null : reader.GetString(reader.GetOrdinal("satellite_type")),
            reader.IsDBNull(reader.GetOrdinal("db_stage")) ? null : reader.GetString(reader.GetOrdinal("db_stage")),
            mongo,
            reader.IsDBNull(reader.GetOrdinal("source_version")) ? null : reader.GetString(reader.GetOrdinal("source_version")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("last_synced_at")),
            reader.GetInt32(reader.GetOrdinal("cached_parameter_count")),
            reader.GetInt32(reader.GetOrdinal("cached_command_count")),
            reader.GetBoolean(reader.GetOrdinal("is_enabled")),
            raw);
    }

    private static CommandCache ReadCommand(NpgsqlDataReader reader)
    {
        var oCmdCode = reader.GetOrdinal("cmd_code");
        var oCmdDesc = reader.GetOrdinal("cmd_desc");
        var oCmdType = reader.GetOrdinal("cmd_type");
        var oCmdLen = reader.GetOrdinal("cmd_len");
        var oExeTime = reader.GetOrdinal("exe_time");
        var oValidFlag = reader.GetOrdinal("valid_flag");
        var oCmdSysId = reader.GetOrdinal("cmd_sys_id");
        var oSource = reader.GetOrdinal("source_version");

        return new CommandCache(
            reader.GetString(reader.GetOrdinal("tasook_no")),
            reader.GetString(reader.GetOrdinal("satellite_no")),
            reader.GetInt32(reader.GetOrdinal("cmd_id")),
            reader.IsDBNull(oCmdCode) ? null : reader.GetString(oCmdCode),
            reader.IsDBNull(oCmdDesc) ? null : reader.GetString(oCmdDesc),
            reader.IsDBNull(oCmdType) ? null : reader.GetInt32(oCmdType),
            reader.IsDBNull(oCmdLen) ? null : reader.GetInt32(oCmdLen),
            reader.IsDBNull(oExeTime) ? null : reader.GetInt32(oExeTime),
            reader.IsDBNull(oValidFlag) ? null : reader.GetInt32(oValidFlag),
            reader.IsDBNull(oCmdSysId) ? null : reader.GetInt32(oCmdSysId),
            reader.IsDBNull(oSource) ? null : reader.GetString(oSource),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("last_synced_at")),
            ParseJson(reader.GetString(reader.GetOrdinal("raw_json"))));
    }

    private static ParamCache ReadParam(NpgsqlDataReader reader)
    {
        var oParaCode = reader.GetOrdinal("para_code");
        var oParaDesc = reader.GetOrdinal("para_desc");
        var oParaTypeDesc = reader.GetOrdinal("para_type_desc");
        var oMin = reader.GetOrdinal("min_value");
        var oMax = reader.GetOrdinal("max_value");
        var oUpdateTime = reader.GetOrdinal("update_time");
        var oProcDesc = reader.GetOrdinal("proc_desc");
        var oPrmSysId = reader.GetOrdinal("prm_sys_id");
        var oSource = reader.GetOrdinal("source_version");

        return new ParamCache(
            reader.GetString(reader.GetOrdinal("tasook_no")),
            reader.GetString(reader.GetOrdinal("satellite_no")),
            reader.GetInt32(reader.GetOrdinal("para_id")),
            reader.IsDBNull(oParaCode) ? null : reader.GetString(oParaCode),
            reader.IsDBNull(oParaDesc) ? null : reader.GetString(oParaDesc),
            reader.IsDBNull(oParaTypeDesc) ? null : reader.GetString(oParaTypeDesc),
            reader.IsDBNull(oMin) ? null : reader.GetDouble(oMin),
            reader.IsDBNull(oMax) ? null : reader.GetDouble(oMax),
            reader.IsDBNull(oUpdateTime) ? null : reader.GetInt32(oUpdateTime),
            reader.IsDBNull(oProcDesc) ? null : reader.GetString(oProcDesc),
            reader.IsDBNull(oPrmSysId) ? null : reader.GetInt32(oPrmSysId),
            reader.IsDBNull(oSource) ? null : reader.GetString(oSource),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("last_synced_at")),
            ParseJson(reader.GetString(reader.GetOrdinal("raw_json"))));
    }

    private static TestBatchCache ReadTestBatch(NpgsqlDataReader reader)
    {
        return new TestBatchCache(
            reader.GetString(reader.GetOrdinal("tasook_no")),
            reader.GetString(reader.GetOrdinal("satellite_no")),
            reader.GetString(reader.GetOrdinal("test_batch_name")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("start_ts")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("end_ts")),
            reader.IsDBNull(reader.GetOrdinal("source_version")) ? null : reader.GetString(reader.GetOrdinal("source_version")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("last_synced_at")),
            ParseJson(reader.GetString(reader.GetOrdinal("raw_json"))));
    }

    private static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        return doc.RootElement.Clone();
    }
}
