using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using SatelliteData.Application.Assets;
using SatelliteData.Domain.Assets;
using SatelliteData.Infrastructure;
using SatelliteData.Infrastructure.HttpClients;

namespace SatelliteData.Infrastructure.PostgreSql;

public sealed class PgDataSourceConfigRepository : IDataSourceConfigRepository
{
    private readonly string _connectionString;
    private readonly AssetProviderOptions _assets;
    private readonly DatabaseConnectionOptions _connections;
    private readonly ILogger<PgDataSourceConfigRepository> _logger;
    private bool _seeded;

    public PgDataSourceConfigRepository(
        IOptions<DatabaseConnectionOptions> databaseConnections,
        IOptions<AssetProviderOptions> assetProviders,
        ILogger<PgDataSourceConfigRepository> logger)
    {
        _connectionString = databaseConnections.Value.Postgres;
        _assets = assetProviders.Value;
        _connections = databaseConnections.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<DataSourceConfig>> GetAllAsync(CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT source_id, source_type, source_name, endpoint_url, auth_type, auth_secret_ref,
                   timeout_ms, enabled, env, created_at, updated_at
            FROM data_source_config
            ORDER BY source_type
            """,
            conn);
        return await ReadAllAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DataSourceConfig?> GetByIdAsync(Guid sourceId, CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT source_id, source_type, source_name, endpoint_url, auth_type, auth_secret_ref,
                   timeout_ms, enabled, env, created_at, updated_at
            FROM data_source_config
            WHERE source_id = @source_id
            """,
            conn);
        cmd.Parameters.AddWithValue("source_id", sourceId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadConfig(reader)
            : null;
    }

    public async Task<DataSourceConfig?> GetEnabledByTypeAsync(string sourceType, CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT source_id, source_type, source_name, endpoint_url, auth_type, auth_secret_ref,
                   timeout_ms, enabled, env, created_at, updated_at
            FROM data_source_config
            WHERE enabled = true AND source_type = @source_type
            LIMIT 1
            """,
            conn);
        cmd.Parameters.AddWithValue("source_type", sourceType);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadConfig(reader)
            : null;
    }

    public async Task SaveAsync(DataSourceConfig config, CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO data_source_config (
                source_id, source_type, source_name, endpoint_url, auth_type, auth_secret_ref,
                timeout_ms, enabled, env, created_at, updated_at)
            VALUES (
                @source_id, @source_type, @source_name, @endpoint_url, @auth_type, @auth_secret_ref,
                @timeout_ms, @enabled, @env, @created_at, @updated_at)
            ON CONFLICT (source_id) DO UPDATE SET
                source_type = EXCLUDED.source_type,
                source_name = EXCLUDED.source_name,
                endpoint_url = EXCLUDED.endpoint_url,
                auth_type = EXCLUDED.auth_type,
                auth_secret_ref = EXCLUDED.auth_secret_ref,
                timeout_ms = EXCLUDED.timeout_ms,
                enabled = EXCLUDED.enabled,
                env = EXCLUDED.env,
                updated_at = EXCLUDED.updated_at
            """,
            conn);
        BindConfig(cmd, config);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid sourceId, CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("DELETE FROM data_source_config WHERE source_id = @source_id", conn);
        cmd.Parameters.AddWithValue("source_id", sourceId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        await PgMetaSchema.EnsureAsync(_connectionString, _logger, cancellationToken).ConfigureAwait(false);
        if (_seeded)
        {
            return;
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await PgMetaSchema.SeedDataSourceConfigsAsync(conn, _assets, _connections, cancellationToken)
            .ConfigureAwait(false);
        _seeded = true;
    }

    private static async Task<IReadOnlyCollection<DataSourceConfig>> ReadAllAsync(
        NpgsqlCommand cmd,
        CancellationToken cancellationToken)
    {
        var list = new List<DataSourceConfig>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadConfig(reader));
        }

        return list;
    }

    private static void BindConfig(NpgsqlCommand cmd, DataSourceConfig config)
    {
        cmd.Parameters.AddWithValue("source_id", config.SourceId);
        cmd.Parameters.AddWithValue("source_type", config.SourceType);
        cmd.Parameters.AddWithValue("source_name", config.SourceName);
        cmd.Parameters.AddWithValue("endpoint_url", config.EndpointUrl);
        cmd.Parameters.AddWithValue("auth_type", config.AuthType);
        cmd.Parameters.AddWithValue("auth_secret_ref", (object?)config.AuthSecretRef ?? DBNull.Value);
        cmd.Parameters.AddWithValue("timeout_ms", config.TimeoutMs);
        cmd.Parameters.AddWithValue("enabled", config.Enabled);
        cmd.Parameters.AddWithValue("env", config.Env);
        cmd.Parameters.AddWithValue("created_at", config.CreatedAt);
        cmd.Parameters.AddWithValue("updated_at", config.UpdatedAt);
    }

    private static DataSourceConfig ReadConfig(NpgsqlDataReader reader)
    {
        var oSecret = reader.GetOrdinal("auth_secret_ref");
        return new DataSourceConfig(
            reader.GetGuid(reader.GetOrdinal("source_id")),
            reader.GetString(reader.GetOrdinal("source_type")),
            reader.GetString(reader.GetOrdinal("source_name")),
            reader.GetString(reader.GetOrdinal("endpoint_url")),
            reader.GetString(reader.GetOrdinal("auth_type")),
            reader.IsDBNull(oSecret) ? null : reader.GetString(oSecret),
            reader.GetInt32(reader.GetOrdinal("timeout_ms")),
            reader.GetBoolean(reader.GetOrdinal("enabled")),
            reader.GetString(reader.GetOrdinal("env")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at")));
    }
}
