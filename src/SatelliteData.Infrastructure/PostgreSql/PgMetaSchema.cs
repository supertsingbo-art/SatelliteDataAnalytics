using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using SatelliteData.Domain.Assets;
using SatelliteData.Domain.Templates;
using SatelliteData.Infrastructure;
using SatelliteData.Infrastructure.HttpClients;

namespace SatelliteData.Infrastructure.PostgreSql;

internal static class PgMetaSchema
{
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS data_source_config (
            source_id uuid PRIMARY KEY,
            source_type varchar(32) NOT NULL,
            source_name varchar(128) NOT NULL,
            endpoint_url text NOT NULL,
            auth_type varchar(32) NOT NULL DEFAULT 'NONE',
            auth_secret_ref varchar(256),
            timeout_ms int NOT NULL DEFAULT 10000,
            enabled boolean NOT NULL DEFAULT true,
            env varchar(32) NOT NULL DEFAULT 'PROD',
            created_at timestamptz NOT NULL,
            updated_at timestamptz NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS uk_data_source_config_type ON data_source_config(source_type);

        CREATE TABLE IF NOT EXISTS filter_template (
            template_id uuid NOT NULL,
            version int NOT NULL,
            template_name varchar(256) NOT NULL,
            status varchar(32) NOT NULL,
            group_id uuid NOT NULL,
            config_json jsonb NOT NULL,
            description text,
            created_by uuid,
            created_at timestamptz NOT NULL,
            updated_by uuid,
            updated_at timestamptz NOT NULL,
            published_at timestamptz,
            PRIMARY KEY (template_id, version)
        );

        CREATE INDEX IF NOT EXISTS idx_filter_template_group ON filter_template(group_id);
        CREATE INDEX IF NOT EXISTS idx_filter_template_status ON filter_template(status);

        CREATE TABLE IF NOT EXISTS algorithm_template (
            template_id uuid NOT NULL,
            version int NOT NULL,
            template_name varchar(256) NOT NULL,
            status varchar(32) NOT NULL,
            react_flow_json jsonb NOT NULL,
            config_json jsonb NOT NULL,
            node_count int NOT NULL DEFAULT 0,
            description text,
            created_by uuid,
            created_at timestamptz NOT NULL,
            updated_by uuid,
            updated_at timestamptz NOT NULL,
            published_at timestamptz,
            PRIMARY KEY (template_id, version)
        );

        CREATE TABLE IF NOT EXISTS algorithm_package (
            package_id uuid PRIMARY KEY,
            algorithm_code varchar(128) NOT NULL,
            algorithm_name varchar(256) NOT NULL,
            algorithm_category varchar(32) NOT NULL,
            version varchar(64) NOT NULL,
            runtime varchar(32) NOT NULL,
            entrypoint varchar(256) NOT NULL,
            object_id uuid NOT NULL,
            manifest_json jsonb NOT NULL,
            inputs_schema_json jsonb NOT NULL,
            outputs_schema_json jsonb NOT NULL,
            params_schema_json jsonb,
            resources_json jsonb NOT NULL,
            status varchar(32) NOT NULL,
            description text,
            last_error text,
            created_by uuid,
            created_at timestamptz NOT NULL,
            updated_by uuid,
            updated_at timestamptz NOT NULL,
            published_at timestamptz
        );

        CREATE UNIQUE INDEX IF NOT EXISTS uk_algorithm_package_code_version ON algorithm_package(algorithm_code, version);
        CREATE INDEX IF NOT EXISTS idx_algorithm_package_code ON algorithm_package(algorithm_code);
        CREATE INDEX IF NOT EXISTS idx_algorithm_package_status ON algorithm_package(status);
        CREATE INDEX IF NOT EXISTS idx_algorithm_package_category ON algorithm_package(algorithm_category);

        ALTER TABLE algorithm_package ADD COLUMN IF NOT EXISTS description text;
        """;

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _ready;

    public static async Task EnsureAsync(
        string connectionString,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (_ready)
        {
            return;
        }

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_ready)
            {
                return;
            }

            await PgSatelliteGroupSchema.EnsureAsync(connectionString, logger, cancellationToken).ConfigureAwait(false);

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (var schema = new NpgsqlCommand(SchemaSql, conn))
            {
                await schema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            _ready = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PostgreSQL 元数据表（模板/数据源/算法包）初始化失败");
            throw;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task SeedDataSourceConfigsAsync(
        NpgsqlConnection conn,
        AssetProviderOptions assets,
        DatabaseConnectionOptions connections,
        CancellationToken cancellationToken)
    {
        await SeedDataSourceRowAsync(
            conn, DataSourceTypes.MassDataApi, "海量数据接口服务-开发", assets.MassDataApiBaseUrl, assets.DefaultDbStage, cancellationToken);
        await SeedDataSourceRowAsync(
            conn,
            DataSourceTypes.SatelliteAssetApi,
            "卫星测试流程规划服务-开发",
            assets.SatelliteAssetApiBaseUrl,
            assets.DefaultDbStage,
            cancellationToken);
        await SeedDataSourceRowAsync(
            conn, DataSourceTypes.ClickHouse, "ClickHouse 分析库-开发", connections.ClickHouse, assets.DefaultDbStage, cancellationToken);
        await SeedDataSourceRowAsync(
            conn, DataSourceTypes.Minio, "MinIO 对象存储-开发", assets.MinioBaseUrl, assets.DefaultDbStage, cancellationToken);
        await SeedDataSourceRowAsync(
            conn, DataSourceTypes.PgMeta, "PostgreSQL 元数据库-开发", connections.Postgres, assets.DefaultDbStage, cancellationToken);
    }

    public static async Task SeedBuiltinAlgorithmPackagesAsync(NpgsqlConnection conn, CancellationToken cancellationToken)
    {
        await using var countCmd = new NpgsqlCommand("SELECT COUNT(*)::int FROM algorithm_package", conn);
        var count = (int)(await countCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0);
        if (count > 0)
        {
            return;
        }

        foreach (var package in AlgorithmPackageBuiltinSeed.CreatePackages())
        {
            await InsertAlgorithmPackageAsync(conn, package, cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task InsertAlgorithmPackageAsync(
        NpgsqlConnection conn,
        AlgorithmPackage package,
        CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO algorithm_package (
                package_id, algorithm_code, algorithm_name, algorithm_category, version, runtime,
                entrypoint, object_id, manifest_json, inputs_schema_json, outputs_schema_json,
                params_schema_json, resources_json, status, description, last_error, created_by, created_at,
                updated_by, updated_at, published_at)
            VALUES (
                @package_id, @algorithm_code, @algorithm_name, @algorithm_category, @version, @runtime,
                @entrypoint, @object_id, @manifest_json, @inputs_schema_json, @outputs_schema_json,
                @params_schema_json, @resources_json, @status, @description, @last_error, @created_by, @created_at,
                @updated_by, @updated_at, @published_at)
            ON CONFLICT (package_id) DO UPDATE SET
                algorithm_code = EXCLUDED.algorithm_code,
                algorithm_name = EXCLUDED.algorithm_name,
                algorithm_category = EXCLUDED.algorithm_category,
                version = EXCLUDED.version,
                runtime = EXCLUDED.runtime,
                entrypoint = EXCLUDED.entrypoint,
                object_id = EXCLUDED.object_id,
                manifest_json = EXCLUDED.manifest_json,
                inputs_schema_json = EXCLUDED.inputs_schema_json,
                outputs_schema_json = EXCLUDED.outputs_schema_json,
                params_schema_json = EXCLUDED.params_schema_json,
                resources_json = EXCLUDED.resources_json,
                status = EXCLUDED.status,
                description = EXCLUDED.description,
                last_error = EXCLUDED.last_error,
                updated_by = EXCLUDED.updated_by,
                updated_at = EXCLUDED.updated_at,
                published_at = EXCLUDED.published_at
            """,
            conn);
        BindAlgorithmPackage(cmd, package);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public static void BindAlgorithmPackage(NpgsqlCommand cmd, AlgorithmPackage package)
    {
        cmd.Parameters.AddWithValue("package_id", package.PackageId);
        cmd.Parameters.AddWithValue("algorithm_code", package.AlgorithmCode);
        cmd.Parameters.AddWithValue("algorithm_name", package.DisplayName);
        cmd.Parameters.AddWithValue("algorithm_category", PgEnumCodec.ToDb(package.Category));
        cmd.Parameters.AddWithValue("version", package.Version);
        cmd.Parameters.AddWithValue("runtime", PgEnumCodec.ToDb(package.Runtime));
        cmd.Parameters.AddWithValue("entrypoint", package.Entrypoint);
        cmd.Parameters.AddWithValue("object_id", package.ObjectId);
        cmd.Parameters.Add("manifest_json", NpgsqlDbType.Jsonb).Value = PgJson.ToJson(package.ManifestJson);
        cmd.Parameters.Add("inputs_schema_json", NpgsqlDbType.Jsonb).Value = PgJson.ToJson(package.InputsSchemaJson);
        cmd.Parameters.Add("outputs_schema_json", NpgsqlDbType.Jsonb).Value = PgJson.ToJson(package.OutputsSchemaJson);
        cmd.Parameters.Add("params_schema_json", NpgsqlDbType.Jsonb).Value = PgJson.ToJson(package.ParamsSchemaJson);
        cmd.Parameters.Add("resources_json", NpgsqlDbType.Jsonb).Value = PgJson.ToJson(package.ResourcesJson);
        cmd.Parameters.AddWithValue("status", PgEnumCodec.ToDb(package.Status));
        cmd.Parameters.AddWithValue("description", (object?)package.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("last_error", (object?)package.LastError ?? DBNull.Value);
        cmd.Parameters.AddWithValue("created_by", (object?)package.UploadedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("created_at", package.CreatedAt);
        cmd.Parameters.AddWithValue("updated_by", (object?)package.UploadedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("updated_at", package.UpdatedAt);
        cmd.Parameters.AddWithValue("published_at", (object?)package.PublishedAt ?? DBNull.Value);
    }

    public static AlgorithmPackage ReadAlgorithmPackage(NpgsqlDataReader reader)
    {
        var oDesc = reader.GetOrdinal("description");
        var oLast = reader.GetOrdinal("last_error");
        var oCreatedBy = reader.GetOrdinal("created_by");
        var oPublished = reader.GetOrdinal("published_at");
        return new AlgorithmPackage(
            reader.GetGuid(reader.GetOrdinal("package_id")),
            reader.GetString(reader.GetOrdinal("algorithm_code")),
            reader.GetString(reader.GetOrdinal("algorithm_name")),
            reader.GetString(reader.GetOrdinal("version")),
            PgEnumCodec.ParseRuntime(reader.GetString(reader.GetOrdinal("runtime"))),
            PgEnumCodec.ParseCategory(reader.GetString(reader.GetOrdinal("algorithm_category"))),
            PgEnumCodec.ParsePackageStatus(reader.GetString(reader.GetOrdinal("status"))),
            PgJson.FromJson(reader.GetString(reader.GetOrdinal("inputs_schema_json"))),
            PgJson.FromJson(reader.GetString(reader.GetOrdinal("outputs_schema_json"))),
            PgJson.FromJson(reader.GetString(reader.GetOrdinal("params_schema_json"))),
            PgJson.FromJson(reader.GetString(reader.GetOrdinal("resources_json"))),
            reader.IsDBNull(oDesc) ? null : reader.GetString(oDesc),
            reader.IsDBNull(oLast) ? null : reader.GetString(oLast),
            reader.IsDBNull(oCreatedBy) ? null : reader.GetGuid(oCreatedBy),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at")),
            reader.IsDBNull(oPublished) ? null : reader.GetFieldValue<DateTimeOffset>(oPublished),
            reader.GetGuid(reader.GetOrdinal("object_id")),
            reader.GetString(reader.GetOrdinal("entrypoint")),
            PgJson.FromJson(reader.GetString(reader.GetOrdinal("manifest_json"))));
    }

    private static async Task SeedDataSourceRowAsync(
        NpgsqlConnection conn,
        string sourceType,
        string sourceName,
        string endpointUrl,
        string env,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO data_source_config (
                source_id, source_type, source_name, endpoint_url, auth_type, auth_secret_ref,
                timeout_ms, enabled, env, created_at, updated_at)
            SELECT @source_id, @source_type, @source_name, @endpoint_url, 'NONE', NULL,
                   10000, true, @env, @created_at, @updated_at
            WHERE NOT EXISTS (SELECT 1 FROM data_source_config WHERE source_type = @source_type)
            """,
            conn);
        cmd.Parameters.AddWithValue("source_id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("source_type", sourceType);
        cmd.Parameters.AddWithValue("source_name", sourceName);
        cmd.Parameters.AddWithValue("endpoint_url", endpointUrl);
        cmd.Parameters.AddWithValue("env", env);
        cmd.Parameters.AddWithValue("created_at", now);
        cmd.Parameters.AddWithValue("updated_at", now);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
