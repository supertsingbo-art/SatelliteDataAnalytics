namespace SatelliteData.Application.Identity;

public sealed record ClientCredentialsTokenCommand(
    string GrantType,
    string AppId,
    string ClientSecret,
    string? RequestedScope,
    string? IpAddress,
    string? UserAgent);

public sealed record OAuthTokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string Scope);

public sealed record OAuthErrorResponse(
    string Error,
    string ErrorDescription,
    int HttpStatus);

public sealed record OAuthTokenResult(
    bool Succeeded,
    OAuthTokenResponse? Token,
    OAuthErrorResponse? Error)
{
    public static OAuthTokenResult Success(OAuthTokenResponse token) => new(true, token, null);

    public static OAuthTokenResult Failure(string error, string description, int httpStatus) =>
        new(false, null, new OAuthErrorResponse(error, description, httpStatus));
}

public sealed record DataScopeCheckRequest(
    string TasookNo,
    string SatelliteNo,
    string? TestBatchId);
