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

    public static string? ExtractDatabaseName(string mongoUri)
    {
        if (!Uri.TryCreate(mongoUri, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var path = uri.AbsolutePath.Trim('/');
        var queryStart = path.IndexOf('?', StringComparison.Ordinal);
        if (queryStart >= 0)
        {
            path = path[..queryStart];
        }

        return string.IsNullOrWhiteSpace(path) ? null : path;
    }
}
