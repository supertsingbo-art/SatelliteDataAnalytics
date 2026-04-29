namespace SatelliteData.Domain.Common;

public sealed record ApiClient(
    Guid ClientId,
    string ClientName,
    string AppId,
    string AppSecretHash,
    IReadOnlyCollection<string> GrantTypes,
    int TokenTtlSeconds,
    bool Enabled,
    IReadOnlyCollection<string> IpAllowlist);

public sealed record OAuthScope(
    Guid ScopeId,
    string ScopeCode,
    string ScopeName,
    string? Description,
    bool Enabled);

public sealed record ApiClientDataScope(
    Guid ScopeId,
    Guid ClientId,
    string TasookNo,
    string SatelliteNo,
    string? TestBatchId,
    string ScopeLevel,
    bool Enabled);

public sealed record OAuthTokenLog(
    Guid TokenLogId,
    Guid? ClientId,
    string GrantType,
    string? RequestedScope,
    string? GrantedScope,
    string? TokenHash,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string? IpAddress,
    string? UserAgent,
    string Result,
    string? ErrorCode,
    string? ErrorDescription);
