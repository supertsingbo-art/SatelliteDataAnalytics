using Hangfire;
using Hangfire.MemoryStorage;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SatelliteData.Application.Pipeline;
using SatelliteData.Application.Tasks;
using SatelliteData.Infrastructure.ClickHouse;
using SatelliteData.Infrastructure.Mongo;

namespace SatelliteData.Infrastructure.Pipeline;

public static class PipelineServiceCollectionExtensions
{
    public static IServiceCollection AddSatellitePipeline(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PipelineOptions>(configuration.GetSection(PipelineOptions.SectionName));
        services.AddSingleton<ClickHouseHttpGateway>();
        services.AddSingleton<ClickHouseBulkWriter>();
        services.AddSingleton<BatchedClickHouseGateway>();
        services.AddSingleton<IClickHouseGateway>(sp => sp.GetRequiredService<BatchedClickHouseGateway>());
        services.AddHostedService<ClickHouseBatchFlushHostedService>();
        services.AddSingleton<IMongoRawSeriesReader, MongoRawSeriesReader>();
        services.AddSingleton<IMongoPkgSeriesReader, MongoPkgSeriesReader>();
        services.AddSingleton<IMongoInstructionSeriesReader, MongoInstructionSeriesReader>();
        services.AddSingleton<IConditionHistoryProvider, MongoConditionHistoryProvider>();
        services.AddSingleton<IFilterRuleEvaluator, FilterRuleEvaluator>();
        services.AddSingleton<RuleTreeSegmentEvaluator>();
        services.AddSingleton<ConditionRangeEvaluator>();
        services.AddSingleton<IOutlierDetector, DefaultOutlierDetector>();

        var po = configuration.GetSection(PipelineOptions.SectionName).Get<PipelineOptions>() ?? new PipelineOptions();
        if (po.UsePostgreSqlTaskStore)
        {
            services.AddSingleton<ITaskRunRepository, PgTaskRunRepository>();
            services.AddSingleton<ITaskEventRepository, PgTaskEventRepository>();
            services.AddSingleton<IHqParamMetadataRepository, PgHqParamMetadataRepository>();
            services.AddSingleton<IPreprocessParamClaimRepository, PgPreprocessParamClaimRepository>();
            services.AddSingleton<IClientCallbackRepository, PgClientCallbackRepository>();
            services.AddSingleton<IPreprocessScheduleRepository, PgPreprocessScheduleRepository>();
            services.AddSingleton<IPreprocessOutlierSegmentRepository, PgPreprocessOutlierSegmentRepository>();
            services.AddSingleton<IPreprocessOutlierPointReviewRepository, PgPreprocessOutlierPointReviewRepository>();
            services.AddSingleton<IPreprocessValidRangeRepository, PgPreprocessValidRangeRepository>();
            services.AddSingleton<IOutlierMarkConfigRepository, PgOutlierMarkConfigRepository>();
        }
        else
        {
            services.AddSingleton<ITaskRunRepository, InMemoryTaskRunRepository>();
            services.AddSingleton<ITaskEventRepository, InMemoryTaskEventRepository>();
            services.AddSingleton<IHqParamMetadataRepository, InMemoryHqParamMetadataRepository>();
            services.AddSingleton<IPreprocessParamClaimRepository, InMemoryPreprocessParamClaimRepository>();
            services.AddSingleton<IClientCallbackRepository, InMemoryClientCallbackRepository>();
            services.AddSingleton<IPreprocessScheduleRepository, InMemoryPreprocessScheduleRepository>();
            services.AddSingleton<IPreprocessOutlierSegmentRepository, InMemoryPreprocessOutlierSegmentRepository>();
            services.AddSingleton<IPreprocessOutlierPointReviewRepository, InMemoryPreprocessOutlierPointReviewRepository>();
            services.AddSingleton<IPreprocessValidRangeRepository, InMemoryPreprocessValidRangeRepository>();
            services.AddSingleton<IOutlierMarkConfigRepository, InMemoryOutlierMarkConfigRepository>();
        }

        services.AddScoped<PreprocessScheduleService>();

        services.AddScoped<IPreprocessPipeline, PreprocessPipeline>();
        services.AddScoped<IAlgorithmExecutionPipeline, AlgorithmExecutionPipeline>();
        services.AddScoped<IWebhookDeliveryPipeline, WebhookDeliveryPipeline>();
        services.AddScoped<TaskOrchestrator>();
        services.AddSingleton<ITaskRunCancellationRegistry, TaskRunCancellationRegistry>();
        services.AddSingleton<IBackgroundJobScheduler, HangfireBackgroundJobScheduler>();
        services.AddTransient<PipelineJobDispatcher>();

        services.AddHttpClient("webhook", client => client.Timeout = TimeSpan.FromSeconds(30));

        services.AddHangfire((sp, hfConfig) =>
        {
            var opts = sp.GetRequiredService<IOptions<PipelineOptions>>().Value;
            hfConfig.SetDataCompatibilityLevel(CompatibilityLevel.Version_180);
            if (string.Equals(opts.Storage, "PostgreSql", StringComparison.OrdinalIgnoreCase))
            {
                var cs = configuration.GetSection(DatabaseConnectionOptions.SectionName)
                    .Get<DatabaseConnectionOptions>()?.Postgres;
                if (string.IsNullOrWhiteSpace(cs))
                {
                    throw new InvalidOperationException("Pipeline.Storage=PostgreSql 需要 ConnectionStrings:Postgres");
                }

                hfConfig.UsePostgreSqlStorage(
                    o => o.UseNpgsqlConnection(cs!),
                    new PostgreSqlStorageOptions { SchemaName = "hangfire" });
            }
            else
            {
                hfConfig.UseMemoryStorage();
            }
        });

        if (po.RunWorkerInApi)
        {
            services.AddHangfireServer(options =>
            {
                options.Queues = ["preprocess", "algorithm", "webhook", "default"];
                options.WorkerCount = Math.Max(Environment.ProcessorCount, 2);
            });
        }

        services.AddHostedService<DevPipelineTemplateSeeder>();

        return services;
    }
}
