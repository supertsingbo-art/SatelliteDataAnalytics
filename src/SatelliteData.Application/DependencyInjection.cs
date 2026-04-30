using Microsoft.Extensions.DependencyInjection;
using SatelliteData.Application.Assets;
using SatelliteData.Application.Identity;
using SatelliteData.Application.Integration;

namespace SatelliteData.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<OAuthTokenService>();
        services.AddScoped<DataScopeAuthorizer>();
        services.AddScoped<DataSourceConfigService>();
        services.AddScoped<AssetSyncService>();
        services.AddScoped<AssetQueryService>();
        services.AddSingleton<MongoConnectionPool>();

        return services;
    }
}
