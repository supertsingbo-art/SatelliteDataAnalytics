using SatelliteData.Domain.Common;

namespace SatelliteData.Application.Identity;

public sealed class OAuthTokenService(
    IApiClientRepository clients,
    ISecretHasher secretHasher,
    IAccessTokenIssuer tokenIssuer)
{
    private static readonly StringComparer ScopeComparer = StringComparer.Ordinal;

    public async Task<OAuthTokenResult> IssueClientCredentialsTokenAsync(
        ClientCredentialsTokenCommand command,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        if (!string.Equals(command.GrantType, "client_credentials", StringComparison.Ordinal))
        {
            await WriteTokenLogAsync(null, command, null, null, now, "FAILED", "invalid_grant",
                "grant_type must be client_credentials", cancellationToken);
            return OAuthTokenResult.Failure("invalid_grant", "grant_type must be client_credentials", 400);
        }

        var client = await clients.FindByAppIdAsync(command.AppId, cancellationToken);
        if (client is null || !client.Enabled || !client.GrantTypes.Contains("client_credentials"))
        {
            await WriteTokenLogAsync(client, command, null, null, now, "FAILED", "invalid_client",
                "client authentication failed", cancellationToken);
            return OAuthTokenResult.Failure("invalid_client", "client authentication failed", 401);
        }

        if (!secretHasher.Verify(command.ClientSecret, client.AppSecretHash))
        {
            await WriteTokenLogAsync(client, command, null, null, now, "FAILED", "invalid_client",
                "client authentication failed", cancellationToken);
            return OAuthTokenResult.Failure("invalid_client", "client authentication failed", 401);
        }

        if (!IsIpAllowed(command.IpAddress, client.IpAllowlist))
        {
            await WriteTokenLogAsync(client, command, null, null, now, "FAILED", "access_denied",
                "client ip is not allowed", cancellationToken);
            return OAuthTokenResult.Failure("access_denied", "client ip is not allowed", 403);
        }

        var authorizedScopes = await clients.GetClientOAuthScopesAsync(client.ClientId, cancellationToken);
        var enabledScopeCodes = authorizedScopes
            .Where(scope => scope.Enabled)
            .Select(scope => scope.ScopeCode)
            .ToHashSet(ScopeComparer);

        var requestedScopes = ParseScopes(command.RequestedScope);
        var grantedScopes = requestedScopes.Count == 0
            ? enabledScopeCodes.OrderBy(scope => scope, ScopeComparer).ToArray()
            : requestedScopes.ToArray();

        if (grantedScopes.Length == 0 || grantedScopes.Any(scope => !enabledScopeCodes.Contains(scope)))
        {
            await WriteTokenLogAsync(client, command, null, null, now, "FAILED", "invalid_scope",
                "requested scope is not authorized", cancellationToken);
            return OAuthTokenResult.Failure("invalid_scope", "requested scope is not authorized", 400);
        }

        var issuedToken = tokenIssuer.Issue(client, grantedScopes, now);
        await WriteTokenLogAsync(client, command, issuedToken.Scope, issuedToken, now, "SUCCESS", null, null,
            cancellationToken);

        return OAuthTokenResult.Success(new OAuthTokenResponse(
            issuedToken.AccessToken,
            "Bearer",
            issuedToken.ExpiresIn,
            issuedToken.Scope));
    }

    private static IReadOnlyCollection<string> ParseScopes(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return Array.Empty<string>();
        }

        return scope
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(ScopeComparer)
            .ToArray();
    }

    private static bool IsIpAllowed(string? ipAddress, IReadOnlyCollection<string> allowlist)
    {
        if (allowlist.Count == 0)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(ipAddress) && allowlist.Contains(ipAddress, StringComparer.Ordinal);
    }

    private Task WriteTokenLogAsync(
        ApiClient? client,
        ClientCredentialsTokenCommand command,
        string? grantedScope,
        IssuedAccessToken? token,
        DateTimeOffset now,
        string result,
        string? errorCode,
        string? errorDescription,
        CancellationToken cancellationToken)
    {
        var log = new OAuthTokenLog(
            Guid.NewGuid(),
            client?.ClientId,
            command.GrantType,
            command.RequestedScope,
            grantedScope,
            token?.TokenHash,
            now,
            token?.ExpiresAt ?? now,
            command.IpAddress,
            command.UserAgent,
            result,
            errorCode,
            errorDescription);

        return clients.AddTokenLogAsync(log, cancellationToken);
    }
}
