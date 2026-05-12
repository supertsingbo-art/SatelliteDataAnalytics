using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SatelliteData.Application.Assets;
using SatelliteData.Application.Identity;
using SatelliteData.Application.Templates;
using SatelliteData.Infrastructure.HttpClients;
using SatelliteData.Infrastructure.Pipeline;
using SatelliteData.Infrastructure.PostgreSql;
using SatelliteData.Infrastructure.Security;
using SatelliteData.Infrastructure.Storage;
using SatelliteData.Application.Algorithms;

namespace SatelliteData.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<OAuthInfrastructureOptions>(configuration.GetSection("OAuth"));
        services.AddSingleton<ISecretHasher, Pbkdf2SecretHasher>();
        services.AddSingleton<IAccessTokenIssuer, ManualJwtTokenService>();
        services.AddSingleton<IAccessTokenValidator, ManualJwtTokenService>();
        services.AddSingleton<IApiClientRepository, InMemoryApiClientRepository>();

        services.Configure<AssetProviderOptions>(configuration.GetSection("AssetProviders"));
        services.Configure<DatabaseConnectionOptions>(configuration.GetSection(DatabaseConnectionOptions.SectionName));
        services.AddSingleton<IDataSourceConfigRepository, InMemoryDataSourceConfigRepository>();
        var usePgAssetCache = configuration.GetValue("AssetCache:UsePostgreSql", false);
        if (usePgAssetCache)
        {
            services.AddSingleton<IAssetCacheRepository, PgAssetCacheRepository>();
        }
        else
        {
            services.AddSingleton<IAssetCacheRepository, InMemoryAssetCacheRepository>();
        }

        services.AddSingleton<ISatelliteGroupRepository, InMemorySatelliteGroupRepository>();
        services.AddSingleton<ISatelliteGroupMemberRepository, InMemorySatelliteGroupMemberRepository>();
        services.AddSingleton<IFilterTemplateRepository, InMemoryFilterTemplateRepository>();
        services.AddSingleton<IAlgorithmTemplateRepository, InMemoryAlgorithmTemplateRepository>();
        services.AddSingleton<IAlgorithmPackageRepository, InMemoryAlgorithmPackageRepository>();

        services.AddHttpClient<IMassDataAssetProvider, MassDataApiClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<AssetProviderOptions>>().Value;
            client.BaseAddress = new Uri(options.MassDataApiBaseUrl.TrimEnd('/'));
        });
        services.AddHttpClient<ISatelliteAssetProvider, SatelliteAssetApiClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<AssetProviderOptions>>().Value;
            client.BaseAddress = new Uri(options.SatelliteAssetApiBaseUrl.TrimEnd('/'));
        });

        services.AddSingleton<IObjectStorageService, InMemoryObjectStorageService>();

        services.AddSatellitePipeline(configuration);

        return services;
    }
}
