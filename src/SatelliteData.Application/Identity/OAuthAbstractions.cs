using System.Security.Claims;
using SatelliteData.Domain.Common;

namespace SatelliteData.Application.Identity;

public interface IApiClientRepository
{
    Task<ApiClient?> FindByAppIdAsync(string appId, CancellationToken cancellationToken);

    Task<ApiClient?> FindByClientIdAsync(Guid clientId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<OAuthScope>> GetClientOAuthScopesAsync(Guid clientId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ApiClientDataScope>> GetClientDataScopesAsync(Guid clientId, CancellationToken cancellationToken);

    Task AddTokenLogAsync(OAuthTokenLog log, CancellationToken cancellationToken);
}

public interface ISecretHasher
{
    string Hash(string secret);

    bool Verify(string secret, string secretHash);
}

public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(ApiClient client, IReadOnlyCollection<string> scopes, DateTimeOffset now);
}

public interface IAccessTokenValidator
{
    TokenValidationResult Validate(string token, DateTimeOffset now);
}

public sealed record IssuedAccessToken(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    int ExpiresIn,
    string Scope,
    string TokenHash);

public sealed record TokenValidationResult(
    bool Succeeded,
    ClaimsPrincipal? Principal,
    string? Error,
    string? ErrorDescription)
{
    public static TokenValidationResult Success(ClaimsPrincipal principal) => new(true, principal, null, null);

    public static TokenValidationResult Failure(string error, string description) => new(false, null, error, description);
}
