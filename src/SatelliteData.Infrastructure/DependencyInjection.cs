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
        services.Configure<AssetCacheOptions>(configuration.GetSection(AssetCacheOptions.SectionName));
        services.Configure<SatelliteGroupOptions>(configuration.GetSection(SatelliteGroupOptions.SectionName));
        services.Configure<InfrastructureStorageOptions>(configuration.GetSection(InfrastructureStorageOptions.SectionName));

        var assetCacheOptions = configuration.GetSection(AssetCacheOptions.SectionName).Get<AssetCacheOptions>()
            ?? new AssetCacheOptions();
        if (assetCacheOptions.UsePostgreSql)
        {
            var pgCs = configuration.GetSection(DatabaseConnectionOptions.SectionName)
                .Get<DatabaseConnectionOptions>()?.Postgres;
            if (string.IsNullOrWhiteSpace(pgCs))
            {
                throw new InvalidOperationException(
                    "AssetCache:UsePostgreSql 为 true 时须配置 ConnectionStrings:Postgres（6.1 资产同步落库）。");
            }

            services.AddSingleton<IAssetCacheRepository, PgAssetCacheRepository>();
        }
        else
        {
            services.AddSingleton<IAssetCacheRepository, InMemoryAssetCacheRepository>();
        }

        var satelliteGroupOptions = configuration.GetSection(SatelliteGroupOptions.SectionName).Get<SatelliteGroupOptions>()
            ?? new SatelliteGroupOptions();
        if (satelliteGroupOptions.UsePostgreSql)
        {
            var groupPgCs = configuration.GetSection(DatabaseConnectionOptions.SectionName)
                .Get<DatabaseConnectionOptions>()?.Postgres;
            if (string.IsNullOrWhiteSpace(groupPgCs))
            {
                throw new InvalidOperationException(
                    "SatelliteGroup:UsePostgreSql 为 true 时须配置 ConnectionStrings:Postgres。");
            }

            services.AddSingleton<ISatelliteGroupRepository, PgSatelliteGroupRepository>();
            services.AddSingleton<ISatelliteGroupMemberRepository, PgSatelliteGroupMemberRepository>();
        }
        else
        {
            services.AddSingleton<ISatelliteGroupRepository, InMemorySatelliteGroupRepository>();
            services.AddSingleton<ISatelliteGroupMemberRepository, InMemorySatelliteGroupMemberRepository>();
        }
        var infrastructureStorageOptions = configuration
            .GetSection(InfrastructureStorageOptions.SectionName)
            .Get<InfrastructureStorageOptions>()
            ?? new InfrastructureStorageOptions();
        if (infrastructureStorageOptions.UsePostgreSql)
        {
            var metaPgCs = configuration.GetSection(DatabaseConnectionOptions.SectionName)
                .Get<DatabaseConnectionOptions>()?.Postgres;
            if (string.IsNullOrWhiteSpace(metaPgCs))
            {
                throw new InvalidOperationException(
                    "InfrastructureStorage:UsePostgreSql 为 true 时须配置 ConnectionStrings:Postgres。");
            }

            services.AddSingleton<IDataSourceConfigRepository, PgDataSourceConfigRepository>();
            services.AddSingleton<IFilterTemplateRepository, PgFilterTemplateRepository>();
            services.AddSingleton<IAlgorithmTemplateRepository, PgAlgorithmTemplateRepository>();
            services.AddSingleton<IAlgorithmPackageRepository, PgAlgorithmPackageRepository>();
        }
        else
        {
            services.AddSingleton<IDataSourceConfigRepository, InMemoryDataSourceConfigRepository>();
            services.AddSingleton<IFilterTemplateRepository, InMemoryFilterTemplateRepository>();
            services.AddSingleton<IAlgorithmTemplateRepository, InMemoryAlgorithmTemplateRepository>();
            services.AddSingleton<IAlgorithmPackageRepository, InMemoryAlgorithmPackageRepository>();
        }

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
