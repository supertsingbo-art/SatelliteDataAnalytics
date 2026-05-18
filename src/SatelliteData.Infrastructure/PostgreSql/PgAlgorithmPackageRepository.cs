using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using SatelliteData.Application.Templates;
using SatelliteData.Domain.Templates;
using SatelliteData.Infrastructure;

namespace SatelliteData.Infrastructure.PostgreSql;

public sealed class PgAlgorithmPackageRepository : IAlgorithmPackageRepository
{
    private const string SelectColumns = """
        package_id, algorithm_code, algorithm_name, algorithm_category, version, runtime, entrypoint,
        object_id, manifest_json, inputs_schema_json, outputs_schema_json, params_schema_json,
        resources_json, status, description, last_error, created_by, created_at, updated_by, updated_at, published_at
        """;

    private readonly string _connectionString;
    private readonly ILogger<PgAlgorithmPackageRepository> _logger;
    private bool _seeded;

    public PgAlgorithmPackageRepository(
        IOptions<DatabaseConnectionOptions> databaseConnections,
        ILogger<PgAlgorithmPackageRepository> logger)
    {
        _connectionString = databaseConnections.Value.Postgres;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<AlgorithmPackage>> GetAllAsync(CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand($"SELECT {SelectColumns} FROM algorithm_package", conn);
        return await ReadAllAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AlgorithmPackage?> GetByIdAsync(Guid packageId, CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM algorithm_package WHERE package_id = @package_id",
            conn);
        cmd.Parameters.AddWithValue("package_id", packageId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? PgMetaSchema.ReadAlgorithmPackage(reader)
            : null;
    }

    public async Task<AlgorithmPackage?> GetByCodeAndVersionAsync(
        string algorithmCode,
        string version,
        CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT package_id, algorithm_code, algorithm_name, algorithm_category, version, runtime, entrypoint,
                   object_id, manifest_json, inputs_schema_json, outputs_schema_json, params_schema_json,
                   resources_json, status, description, last_error, created_by, created_at, updated_by, updated_at, published_at
            FROM algorithm_package
            WHERE algorithm_code = @algorithm_code AND version = @version
            LIMIT 1
            """,
            conn);
        cmd.Parameters.AddWithValue("algorithm_code", algorithmCode);
        cmd.Parameters.AddWithValue("version", version);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? PgMetaSchema.ReadAlgorithmPackage(reader)
            : null;
    }

    public async Task SaveAsync(AlgorithmPackage package, CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await PgMetaSchema.InsertAlgorithmPackageAsync(conn, package, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid packageId, CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("DELETE FROM algorithm_package WHERE package_id = @package_id", conn);
        cmd.Parameters.AddWithValue("package_id", packageId);
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
        await PgMetaSchema.SeedBuiltinAlgorithmPackagesAsync(conn, cancellationToken).ConfigureAwait(false);
        _seeded = true;
    }

    private static async Task<IReadOnlyCollection<AlgorithmPackage>> ReadAllAsync(
        NpgsqlCommand cmd,
        CancellationToken cancellationToken)
    {
        var list = new List<AlgorithmPackage>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(PgMetaSchema.ReadAlgorithmPackage(reader));
        }

        return list;
    }
}
