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
            raw_json jsonb NOT NULL,
            PRIMARY KEY (tasook_no, satellite_no)
        );

        CREATE TABLE IF NOT EXISTS param_cache (
            tasook_no varchar(64) NOT NULL,
            satellite_no varchar(64) NOT NULL,
            param_id varchar(128) NOT NULL,
            param_name varchar(256) NOT NULL,
            unit varchar(64),
            value_type varchar(32),
            value_min double precision,
            value_max double precision,
            source_version varchar(128),
            last_synced_at timestamptz NOT NULL,
            raw_json jsonb NOT NULL,
            PRIMARY KEY (tasook_no, satellite_no, param_id)
        );

        CREATE TABLE IF NOT EXISTS command_cache (
            tasook_no varchar(64) NOT NULL,
            satellite_no varchar(64) NOT NULL,
            command_id varchar(128) NOT NULL,
            command_name varchar(256) NOT NULL,
            source_version varchar(128),
            last_synced_at timestamptz NOT NULL,
            raw_json jsonb NOT NULL,
            PRIMARY KEY (tasook_no, satellite_no, command_id)
        );

        CREATE TABLE IF NOT EXISTS test_batch_cache (
            tasook_no varchar(64) NOT NULL,
            satellite_no varchar(64) NOT NULL,
            test_batch_id varchar(128) NOT NULL,
            scenario varchar(256),
            start_ts timestamptz NOT NULL,
            end_ts timestamptz NOT NULL,
            source_version varchar(128),
            last_synced_at timestamptz NOT NULL,
            raw_json jsonb NOT NULL,
            PRIMARY KEY (tasook_no, satellite_no, test_batch_id)
        );

        ALTER TABLE satellite_cache ADD COLUMN IF NOT EXISTS cached_parameter_count integer NOT NULL DEFAULT 0;
        ALTER TABLE satellite_cache ADD COLUMN IF NOT EXISTS cached_command_count integer NOT NULL DEFAULT 0;
        ALTER TABLE satellite_cache ADD COLUMN IF NOT EXISTS tasook_name varchar(256);
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
                cached_parameter_count, cached_command_count, raw_json)
            VALUES (
                @tasook_no, @tasook_name, @satellite_no, @satellite_name, @satellite_type, @db_stage,
                @mongo_uri, @mongo_db_name, @mongo_auth_ref, @source_version, @last_synced_at,
                @cached_parameter_count, @cached_command_count, @raw_json)
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
                    tasook_no, satellite_no, param_id, param_name, unit, value_type,
                    value_min, value_max, source_version, last_synced_at, raw_json)
                VALUES (
                    @tasook_no, @satellite_no, @param_id, @param_name, @unit, @value_type,
                    @value_min, @value_max, @source_version, @last_synced_at, @raw_json)
                ON CONFLICT (tasook_no, satellite_no, param_id) DO UPDATE SET
                    param_name = EXCLUDED.param_name,
                    unit = EXCLUDED.unit,
                    value_type = EXCLUDED.value_type,
                    value_min = EXCLUDED.value_min,
                    value_max = EXCLUDED.value_max,
                    source_version = EXCLUDED.source_version,
                    last_synced_at = EXCLUDED.last_synced_at,
                    raw_json = EXCLUDED.raw_json;
                """,
                conn,
                tx);

            cmd.Parameters.AddWithValue("tasook_no", p.TasookNo);
            cmd.Parameters.AddWithValue("satellite_no", p.SatelliteNo);
            cmd.Parameters.AddWithValue("param_id", p.ParamId);
            cmd.Parameters.AddWithValue("param_name", p.ParamName);
            cmd.Parameters.AddWithValue("unit", (object?)p.Unit ?? DBNull.Value);
            cmd.Parameters.AddWithValue("value_type", (object?)p.ValueType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("value_min", (object?)p.ValueMin ?? DBNull.Value);
            cmd.Parameters.AddWithValue("value_max", (object?)p.ValueMax ?? DBNull.Value);
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
                    tasook_no, satellite_no, command_id, command_name, source_version, last_synced_at, raw_json)
                VALUES (
                    @tasook_no, @satellite_no, @command_id, @command_name, @source_version, @last_synced_at, @raw_json)
                ON CONFLICT (tasook_no, satellite_no, command_id) DO UPDATE SET
                    command_name = EXCLUDED.command_name,
                    source_version = EXCLUDED.source_version,
                    last_synced_at = EXCLUDED.last_synced_at,
                    raw_json = EXCLUDED.raw_json;
                """,
                conn,
                tx);

            cmd.Parameters.AddWithValue("tasook_no", c.TasookNo);
            cmd.Parameters.AddWithValue("satellite_no", c.SatelliteNo);
            cmd.Parameters.AddWithValue("command_id", c.CommandId);
            cmd.Parameters.AddWithValue("command_name", c.CommandName);
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
                    tasook_no, satellite_no, test_batch_id, scenario, start_ts, end_ts,
                    source_version, last_synced_at, raw_json)
                VALUES (
                    @tasook_no, @satellite_no, @test_batch_id, @scenario, @start_ts, @end_ts,
                    @source_version, @last_synced_at, @raw_json)
                ON CONFLICT (tasook_no, satellite_no, test_batch_id) DO UPDATE SET
                    scenario = EXCLUDED.scenario,
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
            cmd.Parameters.AddWithValue("test_batch_id", t.TestBatchId);
            cmd.Parameters.AddWithValue("scenario", (object?)t.Scenario ?? DBNull.Value);
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
                   cached_parameter_count, cached_command_count, raw_json::text
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

    public async Task<SatelliteCache?> GetSatelliteAsync(string tasookNo, string satelliteNo, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT tasook_no, tasook_name, satellite_no, satellite_name, satellite_type, db_stage,
                   mongo_uri, mongo_db_name, mongo_auth_ref, source_version, last_synced_at,
                   cached_parameter_count, cached_command_count, raw_json::text
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
            SELECT tasook_no, satellite_no, param_id, param_name, unit, value_type,
                   value_min, value_max, source_version, last_synced_at, raw_json::text
            FROM param_cache
            WHERE tasook_no = @tasook_no AND satellite_no = @satellite_no
            ORDER BY param_id;
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

    public async Task<IReadOnlyCollection<TestBatchCache>> GetTestBatchesAsync(string tasookNo, string satelliteNo, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT tasook_no, satellite_no, test_batch_id, scenario, start_ts, end_ts,
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
            SELECT tasook_no, satellite_no, scenario, test_batch_id, start_ts
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
            var scenario = reader.IsDBNull(2) ? null : reader.GetString(2);
            var batchId = reader.GetString(3);
            var startTs = reader.GetFieldValue<DateTimeOffset>(4);
            var label = string.IsNullOrWhiteSpace(scenario) ? batchId : scenario.Trim();
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
            raw);
    }

    private static ParamCache ReadParam(NpgsqlDataReader reader)
    {
        var oMin = reader.GetOrdinal("value_min");
        var oMax = reader.GetOrdinal("value_max");
        return new ParamCache(
            reader.GetString(reader.GetOrdinal("tasook_no")),
            reader.GetString(reader.GetOrdinal("satellite_no")),
            reader.GetString(reader.GetOrdinal("param_id")),
            reader.GetString(reader.GetOrdinal("param_name")),
            reader.IsDBNull(reader.GetOrdinal("unit")) ? null : reader.GetString(reader.GetOrdinal("unit")),
            reader.IsDBNull(reader.GetOrdinal("value_type")) ? null : reader.GetString(reader.GetOrdinal("value_type")),
            reader.IsDBNull(oMin) ? null : reader.GetDouble(oMin),
            reader.IsDBNull(oMax) ? null : reader.GetDouble(oMax),
            reader.IsDBNull(reader.GetOrdinal("source_version")) ? null : reader.GetString(reader.GetOrdinal("source_version")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("last_synced_at")),
            ParseJson(reader.GetString(reader.GetOrdinal("raw_json"))));
    }

    private static TestBatchCache ReadTestBatch(NpgsqlDataReader reader)
    {
        return new TestBatchCache(
            reader.GetString(reader.GetOrdinal("tasook_no")),
            reader.GetString(reader.GetOrdinal("satellite_no")),
            reader.GetString(reader.GetOrdinal("test_batch_id")),
            reader.IsDBNull(reader.GetOrdinal("scenario")) ? null : reader.GetString(reader.GetOrdinal("scenario")),
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
