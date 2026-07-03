using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SatelliteData.Application.Assets;
using SatelliteData.Application.Tasks;
using SatelliteData.Application.Templates;
using SatelliteData.Domain.Templates;

namespace SatelliteData.Infrastructure.Pipeline;

/// <summary>开发环境默认 PIPELINE 所需的已发布筛选/算法模板。</summary>
public sealed class DevPipelineTemplateSeeder(
    IFilterTemplateRepository filterTemplates,
    IAlgorithmTemplateRepository algorithmTemplates,
    ISatelliteGroupRepository groupRepository,
    IAssetCacheRepository assetCacheRepository,
    IServiceScopeFactory scopeFactory,
    ILogger<DevPipelineTemplateSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var root = await groupRepository.GetRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            logger.LogWarning("未找到根分组，跳过默认 PIPELINE 模板种子");
            return;
        }
        var seedReference = await ResolveSeedReferenceAsync(cancellationToken).ConfigureAwait(false);

        var existingFilter = await filterTemplates.GetVersionAsync(PipelineDevIds.DefaultFilterTemplateId, 1, cancellationToken)
            .ConfigureAwait(false);
        if (existingFilter is null)
        {
            if (seedReference is null)
            {
                logger.LogWarning("未找到可用参考卫星与参数缓存，跳过默认筛选模板种子");
            }
            else
            {
                var filterConfig = BuildFilterConfig(root.GroupId, seedReference.Value);
                FilterTemplateValidator.Validate(filterConfig);
                var ft = new FilterTemplate(
                    PipelineDevIds.DefaultFilterTemplateId,
                    1,
                    "默认筛选（PIPELINE 开发）",
                    TemplateStatus.Published,
                    root.GroupId,
                    filterConfig,
                    "DevPipelineTemplateSeeder 种子",
                    null,
                    DateTimeOffset.UtcNow,
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow);
                await filterTemplates.SaveAsync(ft, cancellationToken).ConfigureAwait(false);
                logger.LogInformation(
                    "已种子筛选模板 {Id} v1（参考星 {TasookNo}/{SatelliteNo} paramId={ParamId}）",
                    PipelineDevIds.DefaultFilterTemplateId,
                    seedReference.Value.TasookNo,
                    seedReference.Value.SatelliteNo,
                    seedReference.Value.ParamId);
            }
        }

        var existingAlgo = await algorithmTemplates.GetVersionAsync(PipelineDevIds.DefaultAlgorithmTemplateId, 1, cancellationToken)
            .ConfigureAwait(false);
        if (existingAlgo is null)
        {
            if (seedReference is null)
            {
                logger.LogWarning("未找到可用参数缓存，跳过默认算法模板种子");
            }
            else
            {
                var react = BuildAlgorithmReactFlow(seedReference.Value.ParamId);
                var cfg = JsonDocument.Parse("{}").RootElement.Clone();
                using (var scope = scopeFactory.CreateScope())
                {
                    var validator = scope.ServiceProvider.GetRequiredService<AlgorithmTemplateValidator>();
                    var validation = await validator.ValidateAsync(react, cfg, cancellationToken).ConfigureAwait(false);
                    if (!validation.Valid)
                    {
                        var msg = string.Join("; ", validation.Issues.Select(i => $"{i.Code}:{i.Message}"));
                        logger.LogError("默认算法模板 DAG 校验失败：{Msg}", msg);
                        return;
                    }
                }

                var at = new AlgorithmTemplate(
                    PipelineDevIds.DefaultAlgorithmTemplateId,
                    1,
                    "默认算法（source→threshold）",
                    TemplateStatus.Published,
                    react,
                    cfg,
                    2,
                    "DevPipelineTemplateSeeder 种子",
                    null,
                    DateTimeOffset.UtcNow,
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow);
                await algorithmTemplates.SaveAsync(at, cancellationToken).ConfigureAwait(false);
                logger.LogInformation("已种子算法模板 {Id} v1（source.paramId={ParamId}）", PipelineDevIds.DefaultAlgorithmTemplateId, seedReference.Value.ParamId);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<SeedReference?> ResolveSeedReferenceAsync(CancellationToken cancellationToken)
    {
        var satellites = await assetCacheRepository.GetSatellitesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var satellite in satellites
                     .Where(s => s.IsEnabled)
                     .OrderByDescending(s => s.LastSyncedAt))
        {
            var parameters = await assetCacheRepository
                .GetParametersAsync(satellite.TasookNo, satellite.SatelliteNo, cancellationToken)
                .ConfigureAwait(false);
            var first = parameters.FirstOrDefault();
            if (first is not null)
            {
                return new SeedReference(satellite.TasookNo, satellite.SatelliteNo, first.ParamId);
            }
        }

        return null;
    }

    private static JsonElement BuildFilterConfig(Guid groupId, SeedReference seedReference)
    {
        using var doc = JsonDocument.Parse(
            $$"""
            {
              "scope": {
                "groupId": "{{groupId}}",
                "referenceTasookNo": "{{seedReference.TasookNo}}",
                "referenceSatelliteNo": "{{seedReference.SatelliteNo}}"
              },
              "timeWindow": { "mode": "TEST_BATCH", "bufferBeforeSeconds": 0, "bufferAfterSeconds": 0 },
              "conditionConfig": {
                "instructions": {
                  "enabled": true,
                  "startRelation": "OR",
                  "endRelation": "OR",
                  "startCommands": [],
                  "endCommands": []
                },
                "parametersEnabled": true,
                "parameters": [],
                "expression": ""
              },
              "durationSeconds": 0,
              "targetParams": [
                { "paramId": "{{seedReference.ParamId}}", "outlier": { "method": "SIGMA" } }
              ]
            }
            """);
        return doc.RootElement.Clone();
    }

    private readonly record struct SeedReference(string TasookNo, string SatelliteNo, string ParamId);

    private static JsonElement BuildAlgorithmReactFlow(string paramId)
    {
        using var doc = JsonDocument.Parse(
            $$"""
            {
              "nodes": [
                { "id": "src", "type": "source", "data": { "paramId": "{{paramId}}" } },
                {
                  "id": "out",
                  "type": "output",
                  "data": {
                    "algorithmCode": "threshold_judge",
                    "runtime": "BUILTIN",
                    "params": { "min": 0, "max": 200 }
                  }
                }
              ],
              "edges": [
                { "id": "e1", "source": "src", "target": "out" }
              ]
            }
            """);
        return doc.RootElement.Clone();
    }
}
