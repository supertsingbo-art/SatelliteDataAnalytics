using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SatelliteData.Infrastructure.PostgreSql;

namespace SatelliteData.Infrastructure;

/// <summary>
/// 进程启动时确保 PostgreSQL 目标数据库存在，避免 Schema 初始化阶段抛 3D000。
/// </summary>
public static class DatabaseInitializer
{
    public static async Task EnsurePostgresDatabaseAsync(
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var cs = configuration.GetSection(DatabaseConnectionOptions.SectionName)
            .Get<DatabaseConnectionOptions>()?.Postgres;
        if (string.IsNullOrWhiteSpace(cs))
        {
            return;
        }

        await PgDatabaseBootstrap.EnsureDatabaseExistsAsync(cs, logger, cancellationToken)
            .ConfigureAwait(false);
    }
}
