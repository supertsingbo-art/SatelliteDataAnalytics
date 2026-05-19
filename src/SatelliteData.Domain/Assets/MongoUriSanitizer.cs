using System.Text;
using System.Text.RegularExpressions;

namespace SatelliteData.Domain.Assets;

/// <summary>
/// MongoDB URI 工具。<see cref="StripCredentials"/> 剥离嵌入式 <c>user:password@</c> 凭据，
/// 仅保留 <c>scheme://host(:port)/dbName(?options)</c>，便于落库与日志输出。
/// </summary>
public static class MongoUriSanitizer
{
    private static readonly Regex CredentialsPattern = new(
        @"^(?<scheme>mongodb(\+srv)?://)([^@/]+@)?(?<rest>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string StripCredentials(string mongoUri)
    {
        if (string.IsNullOrWhiteSpace(mongoUri))
        {
            return mongoUri;
        }

        var match = CredentialsPattern.Match(mongoUri.Trim());
        return match.Success
            ? match.Groups["scheme"].Value + match.Groups["rest"].Value
            : mongoUri;
    }

    public static string MaskCredentials(string mongoUri)
    {
        if (string.IsNullOrWhiteSpace(mongoUri))
        {
            return mongoUri;
        }

        var schemeIndex = mongoUri.IndexOf("://", StringComparison.Ordinal);
        var atIndex = mongoUri.IndexOf('@', StringComparison.Ordinal);
        if (schemeIndex > 0 && atIndex > schemeIndex)
        {
            return mongoUri[..(schemeIndex + 3)] + "***:***" + mongoUri[atIndex..];
        }

        return mongoUri;
    }

    /// <summary>
    /// 从 Mongo 连接串解析默认数据库名。会对路径做 <see cref="Uri.UnescapeDataString"/>，
    /// 避免 <see cref="Uri.AbsolutePath"/> 将中文库名保留为 <c>%E6%B5%8B...</c> 导致落库乱码。
    /// </summary>
    public static string? ExtractDatabaseName(string mongoUri)
    {
        if (string.IsNullOrWhiteSpace(mongoUri))
        {
            return null;
        }

        var trimmed = mongoUri.Trim();
        if (!trimmed.StartsWith("mongodb", StringComparison.OrdinalIgnoreCase))
        {
            return TryExtractCatalogFromAdoNetString(trimmed);
        }

        var pathSegment = ExtractMongoPathSegment(trimmed);
        if (string.IsNullOrWhiteSpace(pathSegment))
        {
            return null;
        }

        return DecodeDbNameSegment(pathSegment);
    }

    private static string? ExtractMongoPathSegment(string mongoUri)
    {
        var schemeEnd = mongoUri.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
        {
            return null;
        }

        var authorityAndPath = mongoUri[(schemeEnd + 3)..];
        var at = authorityAndPath.LastIndexOf('@');
        if (at >= 0)
        {
            authorityAndPath = authorityAndPath[(at + 1)..];
        }

        var slash = authorityAndPath.IndexOf('/');
        if (slash < 0 || slash >= authorityAndPath.Length - 1)
        {
            return null;
        }

        var path = authorityAndPath[(slash + 1)..];
        var query = path.IndexOf('?', StringComparison.Ordinal);
        if (query >= 0)
        {
            path = path[..query];
        }

        var hash = path.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0)
        {
            path = path[..hash];
        }

        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    private static string DecodeDbNameSegment(string pathSegment)
    {
        // Uri.AbsolutePath 对非 ASCII 库名常返回百分号编码；需显式解码后再写入 mongo_db_name。
        var decoded = Uri.UnescapeDataString(pathSegment.Replace('+', ' '));
        return TryRepairUtf8Mojibake(decoded);
    }

    /// <summary>
    /// 规范化数据库名：URL 解码后的修补（Latin-1 误读 UTF-8 等）。
    /// </summary>
    public static string NormalizeDbName(string? dbName)
    {
        return string.IsNullOrWhiteSpace(dbName) ? string.Empty : TryRepairUtf8Mojibake(dbName.Trim());
    }

    /// <summary>
    /// 修复 UTF-8 字节被误按 Latin-1 解读产生的乱码（如 æµ‹è¯• → 测试）。
    /// </summary>
    internal static string TryRepairUtf8Mojibake(string value)
    {
        if (string.IsNullOrEmpty(value) || ContainsCjk(value))
        {
            return value;
        }

        if (!value.Any(static c => c > 127))
        {
            return value;
        }

        var bytes = Encoding.Latin1.GetBytes(value);
        var repaired = Encoding.UTF8.GetString(bytes);
        return ContainsCjk(repaired) ? repaired : value;
    }

    private static bool ContainsCjk(string value)
    {
        foreach (var ch in value)
        {
            if (ch is >= '\u4e00' and <= '\u9fff')
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryExtractCatalogFromAdoNetString(string connectionString)
    {
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = part[..eq].Trim();
            if (!key.Equals("Initial Catalog", StringComparison.OrdinalIgnoreCase)
                && !key.Equals("Database", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var catalog = part[(eq + 1)..].Trim();
            return string.IsNullOrWhiteSpace(catalog) ? null : TryRepairUtf8Mojibake(catalog);
        }

        return null;
    }
}
