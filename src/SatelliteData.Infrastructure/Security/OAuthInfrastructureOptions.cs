namespace SatelliteData.Infrastructure.Security;

public sealed class OAuthInfrastructureOptions
{
    public string Issuer { get; init; } = "satellite-data-platform";

    public string Audience { get; init; } = "satellite-data-openapi";

    public string SigningKey { get; init; } = "development-signing-key-change-before-production-32bytes";

    public int DefaultTokenTtlSeconds { get; init; } = 7200;

    public List<SeedApiClientOptions> SeedClients { get; init; } = [];
}

public sealed class SeedApiClientOptions
{
    public string ClientName { get; init; } = "";

    public string AppId { get; init; } = "";

    public string AppSecret { get; init; } = "";

    public int? TokenTtlSeconds { get; init; }

    public List<string> Scopes { get; init; } = [];

    public List<string> IpAllowlist { get; init; } = [];

    public List<SeedClientDataScopeOptions> DataScopes { get; init; } = [];
}

public sealed class SeedClientDataScopeOptions
{
    public string TasookNo { get; init; } = "";

    public string SatelliteNo { get; init; } = "";

    public string? TestBatchId { get; init; }

    public string ScopeLevel { get; init; } = "SATELLITE";
}
