using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using SatelliteData.Application.Templates;
using SatelliteData.Domain.Templates;
using SatelliteData.Infrastructure;

namespace SatelliteData.Infrastructure.PostgreSql;

public sealed class PgFilterTemplateRepository : IFilterTemplateRepository
{
    private const string SelectColumns = """
        template_id, version, template_name, status, group_id, config_json, description,
        created_by, created_at, updated_by, updated_at, published_at
        """;

    private readonly string _connectionString;
    private readonly ILogger<PgFilterTemplateRepository> _logger;

    public PgFilterTemplateRepository(
        IOptions<DatabaseConnectionOptions> databaseConnections,
        ILogger<PgFilterTemplateRepository> logger)
    {
        _connectionString = databaseConnections.Value.Postgres;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<FilterTemplate>> GetAllAsync(CancellationToken cancellationToken)
    {
        await PgMetaSchema.EnsureAsync(_connectionString, _logger, cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand($"SELECT {SelectColumns} FROM filter_template", conn);
        return await ReadAllAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<FilterTemplate>> GetByTemplateIdAsync(
        Guid templateId,
        CancellationToken cancellationToken)
    {
        await PgMetaSchema.EnsureAsync(_connectionString, _logger, cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM filter_template WHERE template_id = @template_id",
            conn);
        cmd.Parameters.AddWithValue("template_id", templateId);
        return await ReadAllAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FilterTemplate?> GetVersionAsync(
        Guid templateId,
        int version,
        CancellationToken cancellationToken)
    {
        await PgMetaSchema.EnsureAsync(_connectionString, _logger, cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM filter_template WHERE template_id = @template_id AND version = @version",
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
            "SELECT COALESCE(MAX(version), 0) FROM filter_template WHERE template_id = @template_id",
            conn);
        cmd.Parameters.AddWithValue("template_id", templateId);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    public async Task SaveAsync(FilterTemplate template, CancellationToken cancellationToken)
    {
        await PgMetaSchema.EnsureAsync(_connectionString, _logger, cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO filter_template (
                template_id, version, template_name, status, group_id, config_json, description,
                created_by, created_at, updated_by, updated_at, published_at)
            VALUES (
                @template_id, @version, @template_name, @status, @group_id, @config_json, @description,
                @created_by, @created_at, @updated_by, @updated_at, @published_at)
            ON CONFLICT (template_id, version) DO UPDATE SET
                template_name = EXCLUDED.template_name,
                status = EXCLUDED.status,
                group_id = EXCLUDED.group_id,
                config_json = EXCLUDED.config_json,
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
            "DELETE FROM filter_template WHERE template_id = @template_id AND version = @version",
            conn);
        cmd.Parameters.AddWithValue("template_id", templateId);
        cmd.Parameters.AddWithValue("version", version);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyCollection<FilterTemplate>> ReadAllAsync(
        NpgsqlCommand cmd,
        CancellationToken cancellationToken)
    {
        var list = new List<FilterTemplate>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadTemplate(reader));
        }

        return list;
    }

    private static void BindTemplate(NpgsqlCommand cmd, FilterTemplate template)
    {
        cmd.Parameters.AddWithValue("template_id", template.TemplateId);
        cmd.Parameters.AddWithValue("version", template.Version);
        cmd.Parameters.AddWithValue("template_name", template.TemplateName);
        cmd.Parameters.AddWithValue("status", PgEnumCodec.ToDb(template.Status));
        cmd.Parameters.AddWithValue("group_id", template.GroupId);
        cmd.Parameters.Add("config_json", NpgsqlDbType.Jsonb).Value = PgJson.ToJson(template.ConfigJson);
        cmd.Parameters.AddWithValue("description", (object?)template.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("created_by", (object?)template.CreatedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("created_at", template.CreatedAt);
        cmd.Parameters.AddWithValue("updated_by", (object?)template.UpdatedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("updated_at", template.UpdatedAt);
        cmd.Parameters.AddWithValue("published_at", (object?)template.PublishedAt ?? DBNull.Value);
    }

    private static FilterTemplate ReadTemplate(NpgsqlDataReader reader)
    {
        var oDesc = reader.GetOrdinal("description");
        var oCreatedBy = reader.GetOrdinal("created_by");
        var oUpdatedBy = reader.GetOrdinal("updated_by");
        var oPublished = reader.GetOrdinal("published_at");
        return new FilterTemplate(
            reader.GetGuid(reader.GetOrdinal("template_id")),
            reader.GetInt32(reader.GetOrdinal("version")),
            reader.GetString(reader.GetOrdinal("template_name")),
            PgEnumCodec.ParseTemplateStatus(reader.GetString(reader.GetOrdinal("status"))),
            reader.GetGuid(reader.GetOrdinal("group_id")),
            PgJson.FromJson(reader.GetString(reader.GetOrdinal("config_json"))),
            reader.IsDBNull(oDesc) ? null : reader.GetString(oDesc),
            reader.IsDBNull(oCreatedBy) ? null : reader.GetGuid(oCreatedBy),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            reader.IsDBNull(oUpdatedBy) ? null : reader.GetGuid(oUpdatedBy),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at")),
            reader.IsDBNull(oPublished) ? null : reader.GetFieldValue<DateTimeOffset>(oPublished));
    }
}
