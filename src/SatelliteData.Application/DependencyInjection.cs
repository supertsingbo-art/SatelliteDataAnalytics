using Microsoft.Extensions.DependencyInjection;
using SatelliteData.Application.Algorithms;
using SatelliteData.Application.Assets;
using SatelliteData.Application.Identity;
using SatelliteData.Application.Integration;
using SatelliteData.Application.Pipeline;
using SatelliteData.Application.Tasks;
using SatelliteData.Application.Templates;

namespace SatelliteData.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<OAuthTokenService>();
        services.AddScoped<DataScopeAuthorizer>();

        // 数据源配置中心（6.1）
        services.AddScoped<DataSourceConfigService>();
        services.AddScoped<AssetSyncService>();
        services.AddScoped<AssetQueryService>();
        services.AddSingleton<MongoConnectionPool>();

        // 模板治理（6.2）
        services.AddScoped<SatelliteGroupService>();
        services.AddScoped<FilterTemplateService>();
        services.AddScoped<AlgorithmTemplateService>();
        services.AddScoped<AlgorithmTemplateValidator>();
        services.AddScoped<AlgorithmRegistryService>();
        services.AddScoped<AlgorithmPackageUploadService>();

        services.AddScoped<PreprocessTaskValidator>();
        services.AddScoped<PreprocessConflictReader>();
        services.AddScoped<PreprocessConflictEnricher>();
        services.AddScoped<PreprocessClaimPlanner>();
        services.AddScoped<PreprocessConflictPreflightService>();
        services.AddScoped<TaskListService>();
        services.AddScoped<TaskExecutionService>();
        services.AddScoped<TaskRunLifecycleService>();
        services.AddScoped<TaskRunProcessedDataService>();
        services.AddScoped<AlgorithmResultQueryService>();
        services.AddScoped<OutlierReviewService>();
        services.AddScoped<OutlierMarkConfigService>();
        services.AddSingleton<ITaskRunConflictOptionStore, InMemoryTaskRunConflictOptionStore>();

        return services;
    }
}
