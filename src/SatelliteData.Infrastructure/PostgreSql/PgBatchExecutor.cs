using Npgsql;

namespace SatelliteData.Infrastructure.PostgreSql;

/// <summary>PostgreSQL 同构语句批量执行（NpgsqlBatch）。</summary>
public static class PgBatchExecutor
{
    public const int DefaultChunkSize = 500;

    public static async Task ExecuteBatchAsync(
        NpgsqlConnection conn,
        string sql,
        IReadOnlyList<Action<NpgsqlBatchCommand>> binders,
        CancellationToken cancellationToken,
        NpgsqlTransaction? tx = null,
        int chunkSize = DefaultChunkSize)
    {
        if (binders.Count == 0)
        {
            return;
        }

        for (var offset = 0; offset < binders.Count; offset += chunkSize)
        {
            await using var batch = new NpgsqlBatch(conn, tx);
            var end = Math.Min(offset + chunkSize, binders.Count);
            for (var i = offset; i < end; i++)
            {
                var cmd = new NpgsqlBatchCommand(sql);
                binders[i](cmd);
                batch.BatchCommands.Add(cmd);
            }

            await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
