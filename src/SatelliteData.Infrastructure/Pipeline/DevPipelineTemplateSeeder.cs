using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SatelliteData.Application.Tasks;
using SatelliteData.Application.Templates;
using SatelliteData.Domain.Templates;

namespace SatelliteData.Infrastructure.Pipeline;

/// <summary>开发环境默认 PIPELINE 所需的已发布筛选/算法模板（与 OAuth 种子数据范围 TASK-A100 / SAT-001 对齐）。</summary>
public sealed class DevPipelineTemplateSeeder(
    IFilterTemplateRepository filterTemplates,
    IAlgorithmTemplateRepository algorithmTemplates,
    ISatelliteGroupRepository groupRepository,
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

        var existingFilter = await filterTemplates.GetVersionAsync(PipelineDevIds.DefaultFilterTemplateId, 1, cancellationToken)
            .ConfigureAwait(false);
        if (existingFilter is null)
        {
            var filterConfig = BuildFilterConfig(root.GroupId);
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
            logger.LogInformation("已种子筛选模板 {Id} v1", PipelineDevIds.DefaultFilterTemplateId);
        }

        var existingAlgo = await algorithmTemplates.GetVersionAsync(PipelineDevIds.DefaultAlgorithmTemplateId, 1, cancellationToken)
            .ConfigureAwait(false);
        if (existingAlgo is null)
        {
            var react = BuildAlgorithmReactFlow();
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
            logger.LogInformation("已种子算法模板 {Id} v1", PipelineDevIds.DefaultAlgorithmTemplateId);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static JsonElement BuildFilterConfig(Guid groupId)
    {
        using var doc = JsonDocument.Parse(
            $$"""
            {
              "scope": {
                "groupId": "{{groupId}}",
                "referenceTasookNo": "TASK-A100",
                "referenceSatelliteNo": "SAT-001"
              },
              "timeWindow": { "mode": "TEST_BATCH", "bufferBeforeSeconds": 0, "bufferAfterSeconds": 0 },
              "ruleTree": { "paramId": "DEMO-PARAM-001", "operator": ">", "value": -1e18 },
              "durationSeconds": 0,
              "targetParams": [
                { "paramId": "DEMO-PARAM-001", "outlier": { "method": "SIGMA" } }
              ]
            }
            """);
        return doc.RootElement.Clone();
    }

    private static JsonElement BuildAlgorithmReactFlow()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "nodes": [
                { "id": "src", "type": "source", "data": { "paramId": "DEMO-PARAM-001" } },
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
