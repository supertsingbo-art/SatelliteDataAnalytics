using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using SatelliteData.Application.Templates;
using SatelliteData.Domain.Templates;

namespace SatelliteData.Infrastructure.PostgreSql;

/// <summary>
/// 卫星分组树 PostgreSQL 实现（<c>ConnectionStrings:Postgres</c>，表 <c>satellite_group</c>）。
/// </summary>
public sealed class PgSatelliteGroupRepository : ISatelliteGroupRepository
{
    private readonly string _connectionString;
    private readonly ILogger<PgSatelliteGroupRepository> _logger;

    public PgSatelliteGroupRepository(
        IOptions<DatabaseConnectionOptions> databaseConnections,
        ILogger<PgSatelliteGroupRepository> logger)
    {
        _connectionString = databaseConnections.Value.Postgres;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<SatelliteGroup>> GetAllAsync(CancellationToken cancellationToken)
    {
        await PgSatelliteGroupSchema.EnsureAsync(_connectionString, _logger, cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT group_id, parent_group_id, group_name, group_path, sort_order, description, created_at, updated_at
            FROM satellite_group
            ORDER BY sort_order, group_name
            """,
            conn);
        return await ReadAllGroupsAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SatelliteGroup?> GetByIdAsync(Guid groupId, CancellationToken cancellationToken)
    {
        await PgSatelliteGroupSchema.EnsureAsync(_connectionString, _logger, cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT group_id, parent_group_id, group_name, group_path, sort_order, description, created_at, updated_at
            FROM satellite_group
            WHERE group_id = @id
            """,
            conn);
        cmd.Parameters.AddWithValue("id", groupId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadGroup(reader)
            : null;
    }

    public async Task<SatelliteGroup?> GetRootAsync(CancellationToken cancellationToken)
    {
        await PgSatelliteGroupSchema.EnsureAsync(_connectionString, _logger, cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT group_id, parent_group_id, group_name, group_path, sort_order, description, created_at, updated_at
            FROM satellite_group
            WHERE parent_group_id IS NULL
            ORDER BY created_at
            LIMIT 2
            """,
            conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        SatelliteGroup? root = null;
        var count = 0;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            count++;
            root = ReadGroup(reader);
        }

        return count > 1 ? throw new InvalidOperationException("存在多个根分组，数据异常") : root;
    }

    public async Task<IReadOnlyCollection<SatelliteGroup>> GetChildrenAsync(
        Guid? parentGroupId,
        CancellationToken cancellationToken)
    {
        await PgSatelliteGroupSchema.EnsureAsync(_connectionString, _logger, cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT group_id, parent_group_id, group_name, group_path, sort_order, description, created_at, updated_at
            FROM satellite_group
            WHERE parent_group_id IS NOT DISTINCT FROM @parent_id
            ORDER BY sort_order, group_name
            """,
            conn);
        cmd.Parameters.AddWithValue("parent_id", (object?)parentGroupId ?? DBNull.Value);
        return await ReadAllGroupsAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(SatelliteGroup group, CancellationToken cancellationToken)
    {
        await PgSatelliteGroupSchema.EnsureAsync(_connectionString, _logger, cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO satellite_group (
                group_id, parent_group_id, group_name, group_path, sort_order, description, created_at, updated_at)
            VALUES (@group_id, @parent_group_id, @group_name, @group_path, @sort_order, @description, @created_at, @updated_at)
            ON CONFLICT (group_id) DO UPDATE SET
                parent_group_id = EXCLUDED.parent_group_id,
                group_name = EXCLUDED.group_name,
                group_path = EXCLUDED.group_path,
                sort_order = EXCLUDED.sort_order,
                description = EXCLUDED.description,
                updated_at = EXCLUDED.updated_at
            """,
            conn);
        BindGroup(cmd, group);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid groupId, CancellationToken cancellationToken)
    {
        await PgSatelliteGroupSchema.EnsureAsync(_connectionString, _logger, cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("DELETE FROM satellite_group WHERE group_id = @id", conn);
        cmd.Parameters.AddWithValue("id", groupId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> HasDirectChildrenAsync(Guid groupId, CancellationToken cancellationToken)
    {
        await PgSatelliteGroupSchema.EnsureAsync(_connectionString, _logger, cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            "SELECT EXISTS(SELECT 1 FROM satellite_group WHERE parent_group_id = @id)",
            conn);
        cmd.Parameters.AddWithValue("id", groupId);
        return await ExecuteExistsAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> SiblingNameExistsAsync(
        Guid? parentGroupId,
        string groupName,
        Guid? excludeGroupId,
        CancellationToken cancellationToken)
    {
        await PgSatelliteGroupSchema.EnsureAsync(_connectionString, _logger, cancellationToken).ConfigureAwait(false);
        var normalizedParent = NormalizeParentId(parentGroupId);
        var trimmedName = groupName.Trim();
        var hasExclude = excludeGroupId.HasValue && excludeGroupId.Value != Guid.Empty;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand { Connection = conn, CommandTimeout = 30 };
        if (hasExclude)
        {
            cmd.CommandText = """
                SELECT EXISTS(
                    SELECT 1 FROM satellite_group
                    WHERE parent_group_id IS NOT DISTINCT FROM @parent_id
                      AND btrim(group_name) = @group_name
                      AND group_id <> @exclude_id
                )
                """;
            cmd.Parameters.Add(new NpgsqlParameter("exclude_id", NpgsqlDbType.Uuid) { Value = excludeGroupId!.Value });
        }
        else
        {
            cmd.CommandText = """
                SELECT EXISTS(
                    SELECT 1 FROM satellite_group
                    WHERE parent_group_id IS NOT DISTINCT FROM @parent_id
                      AND btrim(group_name) = @group_name
                )
                """;
        }

        cmd.Parameters.Add(CreateParentIdParameter(normalizedParent));
        cmd.Parameters.Add(new NpgsqlParameter("group_name", NpgsqlDbType.Varchar) { Value = trimmedName });
        return await ExecuteExistsAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    private static Guid? NormalizeParentId(Guid? parentGroupId) =>
        parentGroupId is null || parentGroupId.Value == Guid.Empty ? null : parentGroupId;

    private static NpgsqlParameter CreateParentIdParameter(Guid? parentGroupId) =>
        new("parent_id", NpgsqlDbType.Uuid) { Value = parentGroupId ?? (object)DBNull.Value };

    private static async Task<bool> ExecuteExistsAsync(NpgsqlCommand cmd, CancellationToken cancellationToken)
    {
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result switch
        {
            bool b => b,
            not null => Convert.ToBoolean(result),
            _ => false
        };
    }

    private static async Task<IReadOnlyCollection<SatelliteGroup>> ReadAllGroupsAsync(
        NpgsqlCommand cmd,
        CancellationToken cancellationToken)
    {
        var list = new List<SatelliteGroup>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadGroup(reader));
        }

        return list;
    }

    private static void BindGroup(NpgsqlCommand cmd, SatelliteGroup group)
    {
        cmd.Parameters.AddWithValue("group_id", group.GroupId);
        cmd.Parameters.AddWithValue("parent_group_id", (object?)group.ParentGroupId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("group_name", group.GroupName);
        cmd.Parameters.AddWithValue("group_path", group.GroupPath);
        cmd.Parameters.AddWithValue("sort_order", group.SortOrder);
        cmd.Parameters.AddWithValue("description", (object?)group.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("created_at", group.CreatedAt);
        cmd.Parameters.AddWithValue("updated_at", group.UpdatedAt);
    }

    private static SatelliteGroup ReadGroup(NpgsqlDataReader reader)
    {
        var oParent = reader.GetOrdinal("parent_group_id");
        var oDesc = reader.GetOrdinal("description");
        return new SatelliteGroup(
            reader.GetGuid(reader.GetOrdinal("group_id")),
            reader.IsDBNull(oParent) ? null : reader.GetGuid(oParent),
            reader.GetString(reader.GetOrdinal("group_name")),
            reader.GetString(reader.GetOrdinal("group_path")),
            reader.GetInt32(reader.GetOrdinal("sort_order")),
            reader.IsDBNull(oDesc) ? null : reader.GetString(oDesc),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at")));
    }
}

/// <summary>
/// 卫星分组成员 PostgreSQL 实现（表 <c>satellite_group_member</c>）。
/// </summary>
public sealed class PgSatelliteGroupMemberRepository : ISatelliteGroupMemberRepository
{
    private readonly string _connectionString;
    private readonly ILogger<PgSatelliteGroupMemberRepository> _logger;

    public PgSatelliteGroupMemberRepository(
        IOptions<DatabaseConnectionOptions> databaseConnections,
        ILogger<PgSatelliteGroupMemberRepository> logger)
    {
        _connectionString = databaseConnections.Value.Postgres;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<SatelliteGroupMember>> GetAllAsync(CancellationToken cancellationToken)
    {
        await PgSatelliteGroupSchema.EnsureAsync(_connectionString, _logger, cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            "SELECT tasook_no, satellite_no, group_id, created_at FROM satellite_group_member",
            conn);
        return await ReadAllMembersAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<SatelliteGroupMember>> GetByGroupAsync(
        Guid groupId,
        CancellationToken cancellationToken)
    {
        await PgSatelliteGroupSchema.EnsureAsync(_connectionString, _logger, cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT tasook_no, satellite_no, group_id, created_at
            FROM satellite_group_member
            WHERE group_id = @group_id
            """,
            conn);
        cmd.Parameters.AddWithValue("group_id", groupId);
        return await ReadAllMembersAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SatelliteGroupMember?> GetMembershipAsync(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken)
    {
        await PgSatelliteGroupSchema.EnsureAsync(_connectionString, _logger, cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT tasook_no, satellite_no, group_id, created_at
            FROM satellite_group_member
            WHERE tasook_no = @tasook_no AND satellite_no = @satellite_no
            """,
            conn);
        cmd.Parameters.AddWithValue("tasook_no", tasookNo);
        cmd.Parameters.AddWithValue("satellite_no", satelliteNo);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadMember(reader)
            : null;
    }

    public async Task UpsertAsync(SatelliteGroupMember member, CancellationToken cancellationToken)
    {
        await PgSatelliteGroupSchema.EnsureAsync(_connectionString, _logger, cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO satellite_group_member (tasook_no, satellite_no, group_id, created_at)
            VALUES (@tasook_no, @satellite_no, @group_id, @created_at)
            ON CONFLICT (tasook_no, satellite_no) DO UPDATE SET
                group_id = EXCLUDED.group_id,
                created_at = EXCLUDED.created_at
            """,
            conn);
        cmd.Parameters.AddWithValue("tasook_no", member.TasookNo);
        cmd.Parameters.AddWithValue("satellite_no", member.SatelliteNo);
        cmd.Parameters.AddWithValue("group_id", member.GroupId);
        cmd.Parameters.AddWithValue("created_at", member.CreatedAt);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string tasookNo, string satelliteNo, CancellationToken cancellationToken)
    {
        await PgSatelliteGroupSchema.EnsureAsync(_connectionString, _logger, cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM satellite_group_member WHERE tasook_no = @tasook_no AND satellite_no = @satellite_no",
            conn);
        cmd.Parameters.AddWithValue("tasook_no", tasookNo);
        cmd.Parameters.AddWithValue("satellite_no", satelliteNo);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CountByGroupAsync(Guid groupId, CancellationToken cancellationToken)
    {
        await PgSatelliteGroupSchema.EnsureAsync(_connectionString, _logger, cancellationToken).ConfigureAwait(false);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*)::int FROM satellite_group_member WHERE group_id = @group_id",
            conn);
        cmd.Parameters.AddWithValue("group_id", groupId);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is int count ? count : Convert.ToInt32(result);
    }

    private static async Task<IReadOnlyCollection<SatelliteGroupMember>> ReadAllMembersAsync(
        NpgsqlCommand cmd,
        CancellationToken cancellationToken)
    {
        var list = new List<SatelliteGroupMember>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadMember(reader));
        }

        return list;
    }

    private static SatelliteGroupMember ReadMember(NpgsqlDataReader reader) =>
        new(
            reader.GetString(reader.GetOrdinal("tasook_no")),
            reader.GetString(reader.GetOrdinal("satellite_no")),
            reader.GetGuid(reader.GetOrdinal("group_id")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")));
}

internal static class PgSatelliteGroupSchema
{
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS satellite_group (
            group_id uuid PRIMARY KEY,
            parent_group_id uuid REFERENCES satellite_group(group_id),
            group_name varchar(256) NOT NULL,
            group_path varchar(1024) NOT NULL,
            sort_order int NOT NULL DEFAULT 0,
            description text,
            created_at timestamptz NOT NULL,
            updated_at timestamptz NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS uk_satellite_group_path ON satellite_group(group_path);
        CREATE UNIQUE INDEX IF NOT EXISTS uk_satellite_group_sibling_name ON satellite_group(parent_group_id, group_name);
        CREATE INDEX IF NOT EXISTS idx_satellite_group_parent ON satellite_group(parent_group_id);

        CREATE TABLE IF NOT EXISTS satellite_group_member (
            tasook_no varchar(64) NOT NULL,
            satellite_no varchar(64) NOT NULL,
            group_id uuid NOT NULL REFERENCES satellite_group(group_id),
            created_at timestamptz NOT NULL,
            PRIMARY KEY (tasook_no, satellite_no)
        );

        CREATE INDEX IF NOT EXISTS idx_satellite_group_member_group ON satellite_group_member(group_id);
        """;

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static volatile bool _ready;

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

            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            if (builder.Timeout <= 0)
            {
                builder.Timeout = 15;
            }

            await using var conn = new NpgsqlConnection(builder.ConnectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (var schema = new NpgsqlCommand(SchemaSql, conn) { CommandTimeout = 60 })
            {
                await schema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await SeedDefaultRootAsync(conn, cancellationToken).ConfigureAwait(false);
            _ready = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PostgreSQL 卫星分组表初始化失败");
            throw;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task SeedDefaultRootAsync(NpgsqlConnection conn, CancellationToken cancellationToken)
    {
        await using var exists = new NpgsqlCommand(
            "SELECT 1 FROM satellite_group WHERE parent_group_id IS NULL LIMIT 1",
            conn);
        var hasRoot = await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (hasRoot is not null)
        {
            return;
        }

        var rootId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO satellite_group (
                group_id, parent_group_id, group_name, group_path, sort_order, description, created_at, updated_at)
            VALUES (@group_id, NULL, @group_name, @group_path, 0, @description, @created_at, @updated_at)
            """,
            conn);
        insert.Parameters.AddWithValue("group_id", rootId);
        insert.Parameters.AddWithValue("group_name", SatelliteGroupConstants.DefaultRootName);
        insert.Parameters.AddWithValue("group_path", SatelliteGroupConstants.DefaultRootPath);
        insert.Parameters.AddWithValue(
            "description",
            "系统初始化时创建的默认根分组；未显式归组的卫星挂在此分组下");
        insert.Parameters.AddWithValue("created_at", now);
        insert.Parameters.AddWithValue("updated_at", now);
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
