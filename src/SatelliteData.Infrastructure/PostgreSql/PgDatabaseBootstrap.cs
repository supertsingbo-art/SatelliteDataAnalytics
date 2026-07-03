using Microsoft.Extensions.Logging;
using Npgsql;

namespace SatelliteData.Infrastructure.PostgreSql;

/// <summary>
/// 启动时确保目标 PostgreSQL 数据库存在；不存在则连接维护库 postgres 自动创建。
/// 解决 Schema 初始化直接 OpenAsync 目标库时抛 3D000 (database does not exist) 的问题。
/// </summary>
internal static class PgDatabaseBootstrap
{
    private const string MaintenanceDatabase = "postgres";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly HashSet<string> VerifiedDatabases = new(StringComparer.Ordinal);

    public static async Task EnsureDatabaseExistsAsync(
        string connectionString,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var database = builder.Database;
        if (string.IsNullOrWhiteSpace(database))
        {
            return;
        }

        if (VerifiedDatabases.Contains(database))
        {
            return;
        }

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (VerifiedDatabases.Contains(database))
            {
                return;
            }

            await EnsureCoreAsync(builder, database, logger, cancellationToken).ConfigureAwait(false);
            VerifiedDatabases.Add(database);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task EnsureCoreAsync(
        NpgsqlConnectionStringBuilder builder,
        string database,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var adminBuilder = new NpgsqlConnectionStringBuilder(builder.ConnectionString)
        {
            Database = MaintenanceDatabase,
            Pooling = false
        };
        if (adminBuilder.Timeout <= 0)
        {
            adminBuilder.Timeout = 15;
        }

        try
        {
            await using var conn = new NpgsqlConnection(adminBuilder.ConnectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

            if (await DatabaseExistsAsync(conn, database, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            var quotedIdentifier = QuoteIdentifier(database);
            await using var createCmd = new NpgsqlCommand($"CREATE DATABASE {quotedIdentifier}", conn)
            {
                CommandTimeout = 60
            };
            await createCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("PostgreSQL 数据库 {Database} 不存在，已自动创建", database);
        }
        catch (PostgresException ex) when (ex.SqlState == "42P04")
        {
            // 并发竞态：另一个进程/连接刚刚创建了同名库
            logger.LogInformation("PostgreSQL 数据库 {Database} 已被并发创建", database);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PostgreSQL 数据库 {Database} 自动创建失败", database);
            throw;
        }
    }

    private static async Task<bool> DatabaseExistsAsync(
        NpgsqlConnection conn,
        string database,
        CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT 1 FROM pg_database WHERE datname = @name",
            conn);
        cmd.Parameters.AddWithValue("name", database);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null && result != DBNull.Value;
    }

    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }
}
