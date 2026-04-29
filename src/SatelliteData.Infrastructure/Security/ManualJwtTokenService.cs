using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SatelliteData.Application.Identity;
using SatelliteData.Domain.Common;

namespace SatelliteData.Infrastructure.Security;

public sealed class ManualJwtTokenService(IOptions<OAuthInfrastructureOptions> options) :
    IAccessTokenIssuer,
    IAccessTokenValidator
{
    private readonly OAuthInfrastructureOptions _options = options.Value;

    public IssuedAccessToken Issue(ApiClient client, IReadOnlyCollection<string> scopes, DateTimeOffset now)
    {
        var expiresAt = now.AddSeconds(client.TokenTtlSeconds);
        var scopeText = string.Join(' ', scopes);
        var jti = Guid.NewGuid().ToString("N");

        var header = new Dictionary<string, object>
        {
            ["alg"] = "HS256",
            ["typ"] = "JWT"
        };

        var payload = new Dictionary<string, object>
        {
            ["iss"] = _options.Issuer,
            ["aud"] = _options.Audience,
            ["sub"] = client.ClientId.ToString(),
            ["client_id"] = client.ClientId.ToString(),
            ["app_id"] = client.AppId,
            ["scope"] = scopeText,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = expiresAt.ToUnixTimeSeconds(),
            ["jti"] = jti
        };

        var encodedHeader = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
        var encodedPayload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signature = Sign($"{encodedHeader}.{encodedPayload}");
        var token = $"{encodedHeader}.{encodedPayload}.{signature}";

        return new IssuedAccessToken(
            token,
            expiresAt,
            client.TokenTtlSeconds,
            scopeText,
            HashToken(token));
    }

    public TokenValidationResult Validate(string token, DateTimeOffset now)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return TokenValidationResult.Failure("invalid_token", "token format is invalid");
        }

        var expectedSignature = Sign($"{parts[0]}.{parts[1]}");
        if (!FixedTimeEquals(expectedSignature, parts[2]))
        {
            return TokenValidationResult.Failure("invalid_token", "token signature is invalid");
        }

        Dictionary<string, JsonElement>? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Base64UrlDecode(parts[1]));
        }
        catch (JsonException)
        {
            return TokenValidationResult.Failure("invalid_token", "token payload is invalid");
        }

        if (payload is null)
        {
            return TokenValidationResult.Failure("invalid_token", "token payload is invalid");
        }

        if (!StringClaimEquals(payload, "iss", _options.Issuer) || !StringClaimEquals(payload, "aud", _options.Audience))
        {
            return TokenValidationResult.Failure("invalid_token", "token issuer or audience is invalid");
        }

        if (!payload.TryGetValue("exp", out var expValue) || expValue.GetInt64() <= now.ToUnixTimeSeconds())
        {
            return TokenValidationResult.Failure("invalid_token", "token is expired");
        }

        var claims = new List<Claim>();
        foreach (var (key, value) in payload)
        {
            claims.Add(new Claim(key, value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString()));
        }

        var identity = new ClaimsIdentity(claims, "Bearer", "sub", "scope");
        return TokenValidationResult.Success(new ClaimsPrincipal(identity));
    }

    private string Sign(string content)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.SigningKey));
        return Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(content)));
    }

    private static string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool StringClaimEquals(Dictionary<string, JsonElement> payload, string claim, string expected)
    {
        return payload.TryGetValue(claim, out var value)
            && value.ValueKind == JsonValueKind.String
            && string.Equals(value.GetString(), expected, StringComparison.Ordinal);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.ASCII.GetBytes(left);
        var rightBytes = Encoding.ASCII.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }
}
