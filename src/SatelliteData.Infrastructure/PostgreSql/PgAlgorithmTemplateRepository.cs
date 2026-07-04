using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using SatelliteData.Application.Templates;
using SatelliteData.Domain.Templates;
using SatelliteData.Infrastructure;

namespace SatelliteData.Infrastructure.PostgreSql;

public sealed class PgAlgorithmTemplateRepository : IAlgorithmTemplateRepository
{
    private const string SelectColumns = """
        template_id, version, template_name, status, react_flow_json, config_json, node_count,
        description, created_by, created_at, updated_by, updated_at, published_at
        """;

    private readonly string _connectionString;
    private readonly ILogger<PgAlgorithmTemplateRepository> _logger;

    public PgAlgorithmTemplateRepository(
        IOptions<DatabaseConnectionOptions> databaseConnections,
        ILogger<PgAlgorithmTemplateRepository> logger)
    {
        _connectionString = databaseConnections.Value.Postgres;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<AlgorithmTemplate>> GetAllAsync(CancellationToken cancellationToken)
    {
        await PgMetaSchema.EnsureAsync(_connectionString, _logger, cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand($"SELECT {SelectColumns} FROM algorithm_template", conn);
        return await ReadAllAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<AlgorithmTemplate>> GetByTemplateIdAsync(
        Guid templateId,
        CancellationToken cancellationToken)
    {
        await PgMetaSchema.EnsureAsync(_connectionString, _logger, cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM algorithm_template WHERE template_id = @template_id",
            conn);
        cmd.Parameters.AddWithValue("template_id", templateId);
        return await ReadAllAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AlgorithmTemplate?> GetVersionAsync(
        Guid templateId,
        int version,
        CancellationToken cancellationToken)
    {
        await PgMetaSchema.EnsureAsync(_connectionString, _logger, cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM algorithm_template WHERE template_id = @template_id AND version = @version",
            conn);
        cmd.Parameters.AddWithValue("template_id", templateId);
        cmd.Parameters.AddWithValue("version", version);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadTemplate(reader)
            : null;
    }

    public async Task<int> GetMaxVersionAsync(Guid templateId, CancellationToken cancellationToken)
    {
        await PgMetaSchema.EnsureAsync(_connectionString, _logger, cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            "SELECT COALESCE(MAX(version), 0) FROM algorithm_template WHERE template_id = @template_id",
            conn);
        cmd.Parameters.AddWithValue("template_id", templateId);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    public async Task SaveAsync(AlgorithmTemplate template, CancellationToken cancellationToken)
    {
        await PgMetaSchema.EnsureAsync(_connectionString, _logger, cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO algorithm_template (
                template_id, version, template_name, status, react_flow_json, config_json, node_count,
                description, created_by, created_at, updated_by, updated_at, published_at)
            VALUES (
                @template_id, @version, @template_name, @status, @react_flow_json, @config_json, @node_count,
                @description, @created_by, @created_at, @updated_by, @updated_at, @published_at)
            ON CONFLICT (template_id, version) DO UPDATE SET
                template_name = EXCLUDED.template_name,
                status = EXCLUDED.status,
                react_flow_json = EXCLUDED.react_flow_json,
                config_json = EXCLUDED.config_json,
                node_count = EXCLUDED.node_count,
                description = EXCLUDED.description,
                updated_by = EXCLUDED.updated_by,
                updated_at = EXCLUDED.updated_at,
                published_at = EXCLUDED.published_at
            """,
            conn);
        BindTemplate(cmd, template);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid templateId, int version, CancellationToken cancellationToken)
    {
        await PgMetaSchema.EnsureAsync(_connectionString, _logger, cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM algorithm_template WHERE template_id = @template_id AND version = @version",
            conn);
        cmd.Parameters.AddWithValue("template_id", templateId);
        cmd.Parameters.AddWithValue("version", version);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAllByTemplateIdAsync(Guid templateId, CancellationToken cancellationToken)
    {
        await PgMetaSchema.EnsureAsync(_connectionString, _logger, cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM algorithm_template WHERE template_id = @template_id",
            conn);
        cmd.Parameters.AddWithValue("template_id", templateId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyCollection<AlgorithmTemplate>> ReadAllAsync(
        NpgsqlCommand cmd,
        CancellationToken cancellationToken)
    {
        var list = new List<AlgorithmTemplate>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadTemplate(reader));
        }

        return list;
    }

    private static void BindTemplate(NpgsqlCommand cmd, AlgorithmTemplate template)
    {
        cmd.Parameters.AddWithValue("template_id", template.TemplateId);
        cmd.Parameters.AddWithValue("version", template.Version);
        cmd.Parameters.AddWithValue("template_name", template.TemplateName);
        cmd.Parameters.AddWithValue("status", PgEnumCodec.ToDb(template.Status));
        cmd.Parameters.Add("react_flow_json", NpgsqlDbType.Jsonb).Value = PgJson.ToJson(template.ReactFlowJson);
        cmd.Parameters.Add("config_json", NpgsqlDbType.Jsonb).Value = PgJson.ToJson(template.ConfigJson);
        cmd.Parameters.AddWithValue("node_count", template.NodeCount);
        cmd.Parameters.AddWithValue("description", (object?)template.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("created_by", (object?)template.CreatedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("created_at", template.CreatedAt);
        cmd.Parameters.AddWithValue("updated_by", (object?)template.UpdatedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("updated_at", template.UpdatedAt);
        cmd.Parameters.AddWithValue("published_at", (object?)template.PublishedAt ?? DBNull.Value);
    }

    private static AlgorithmTemplate ReadTemplate(NpgsqlDataReader reader)
    {
        var oDesc = reader.GetOrdinal("description");
        var oCreatedBy = reader.GetOrdinal("created_by");
        var oUpdatedBy = reader.GetOrdinal("updated_by");
        var oPublished = reader.GetOrdinal("published_at");
        return new AlgorithmTemplate(
            reader.GetGuid(reader.GetOrdinal("template_id")),
            reader.GetInt32(reader.GetOrdinal("version")),
            reader.GetString(reader.GetOrdinal("template_name")),
            PgEnumCodec.ParseTemplateStatus(reader.GetString(reader.GetOrdinal("status"))),
            PgJson.FromJson(reader.GetString(reader.GetOrdinal("react_flow_json"))),
            PgJson.FromJson(reader.GetString(reader.GetOrdinal("config_json"))),
            reader.GetInt32(reader.GetOrdinal("node_count")),
            reader.IsDBNull(oDesc) ? null : reader.GetString(oDesc),
            reader.IsDBNull(oCreatedBy) ? null : reader.GetGuid(oCreatedBy),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            reader.IsDBNull(oUpdatedBy) ? null : reader.GetGuid(oUpdatedBy),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at")),
            reader.IsDBNull(oPublished) ? null : reader.GetFieldValue<DateTimeOffset>(oPublished));
    }
}
