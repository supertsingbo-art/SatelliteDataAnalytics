using System.Text.Json;
using SatelliteData.Application.Templates;
using SatelliteData.Infrastructure.PostgreSql;
using Xunit;

namespace SatelliteData.UnitTests;

public sealed class AlgorithmTemplateValidatorTests
{
    [Fact]
    public async Task ValidateAsync_AcceptsSaveResultAsDataOutputNode()
    {
        var registry = new AlgorithmRegistryService(new InMemoryAlgorithmPackageRepository());
        var validator = new AlgorithmTemplateValidator(registry);
        var reactFlow = ParseJson(
            """
            {
              "nodes": [
                { "id": "src_1", "type": "source", "data": { "nodeRef": "in_1", "paramId": "1001" } },
                { "id": "mean_1", "type": "stats", "data": { "algorithmCode": "mean", "runtime": "BUILTIN" } },
                { "id": "sink_1", "type": "dataoutput", "data": { "algorithmCode": "save_result", "runtime": "BUILTIN", "params": { "metricName": "均值" } } }
              ],
              "edges": [
                { "id": "e1", "source": "src_1", "target": "mean_1" },
                { "id": "e2", "source": "mean_1", "target": "sink_1" }
              ]
            }
            """);
        var config = SingleSourceConfig("in_1", "1001");

        var result = await validator.ValidateAsync(reactFlow, config, CancellationToken.None);

        Assert.True(result.Valid);
    }

    [Fact]
    public async Task ValidateAsync_AcceptsLegacySaveResultAsOutputNode()
    {
        var registry = new AlgorithmRegistryService(new InMemoryAlgorithmPackageRepository());
        var validator = new AlgorithmTemplateValidator(registry);
        var reactFlow = ParseJson(
            """
            {
              "nodes": [
                { "id": "src_1", "type": "source", "data": { "paramId": "1001" } },
                { "id": "mean_1", "type": "stats", "data": { "algorithmCode": "mean", "runtime": "BUILTIN" } },
                { "id": "sink_1", "type": "output", "data": { "algorithmCode": "save_result", "runtime": "BUILTIN", "params": { "metricName": "均值" } } }
              ],
              "edges": [
                { "id": "e1", "source": "src_1", "target": "mean_1" },
                { "id": "e2", "source": "mean_1", "target": "sink_1" }
              ]
            }
            """);
        var config = ParseJson("{}");

        var result = await validator.ValidateAsync(reactFlow, config, CancellationToken.None);

        Assert.True(result.Valid);
    }

    [Fact]
    public async Task ValidateAsync_RequiresDataOutputNode()
    {
        var registry = new AlgorithmRegistryService(new InMemoryAlgorithmPackageRepository());
        var validator = new AlgorithmTemplateValidator(registry);
        var reactFlow = ParseJson(
            """
            {
              "nodes": [
                { "id": "src_1", "type": "source", "data": { "nodeRef": "in_1" } },
                { "id": "mean_1", "type": "stats", "data": { "algorithmCode": "mean", "runtime": "BUILTIN" } }
              ],
              "edges": [
                { "id": "e1", "source": "src_1", "target": "mean_1" }
              ]
            }
            """);
        var config = SingleSourceConfig("in_1", "1001");

        var result = await validator.ValidateAsync(reactFlow, config, CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains(result.Issues, i => i.Code == "DAG_009");
    }

    [Fact]
    public async Task ValidateAsync_SaveResultRequiresExactlyOneIncomingEdge()
    {
        var registry = new AlgorithmRegistryService(new InMemoryAlgorithmPackageRepository());
        var validator = new AlgorithmTemplateValidator(registry);
        var reactFlow = ParseJson(
            """
            {
              "nodes": [
                { "id": "src_1", "type": "source", "data": { "paramId": "1001" } },
                { "id": "sink_1", "type": "output", "data": { "algorithmCode": "save_result", "runtime": "BUILTIN" } }
              ],
              "edges": []
            }
            """);
        var config = ParseJson("{}");

        var result = await validator.ValidateAsync(reactFlow, config, CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains(result.Issues, i => i.Code == "DAG_008");
    }

    [Fact]
    public async Task ValidateAsync_RequiresSourceParam()
    {
        var registry = new AlgorithmRegistryService(new InMemoryAlgorithmPackageRepository());
        var validator = new AlgorithmTemplateValidator(registry);
        var reactFlow = ParseJson(
            """
            {
              "nodes": [
                { "id": "src_1", "type": "source", "data": { "nodeRef": "in_1" } },
                { "id": "mean_1", "type": "stats", "data": { "algorithmCode": "mean", "runtime": "BUILTIN" } },
                { "id": "sink_1", "type": "dataoutput", "data": { "algorithmCode": "save_result", "runtime": "BUILTIN" } }
              ],
              "edges": [
                { "id": "e1", "source": "src_1", "target": "mean_1" },
                { "id": "e2", "source": "mean_1", "target": "sink_1" }
              ]
            }
            """);
        var config = ParseJson(
            """
            {
              "dataInputs": [
                { "nodeRef": "in_1", "paramIds": [] }
              ]
            }
            """);

        var result = await validator.ValidateAsync(reactFlow, config, CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains(result.Issues, i => i.Code == "DAG_010");
    }

    [Fact]
    public async Task ValidateAsync_RejectsMultipleSourceParams()
    {
        var registry = new AlgorithmRegistryService(new InMemoryAlgorithmPackageRepository());
        var validator = new AlgorithmTemplateValidator(registry);
        var reactFlow = ParseJson(
            """
            {
              "nodes": [
                { "id": "src_1", "type": "source", "data": { "nodeRef": "in_1" } },
                { "id": "mean_1", "type": "stats", "data": { "algorithmCode": "mean", "runtime": "BUILTIN" } },
                { "id": "sink_1", "type": "dataoutput", "data": { "algorithmCode": "save_result", "runtime": "BUILTIN" } }
              ],
              "edges": [
                { "id": "e1", "source": "src_1", "target": "mean_1" },
                { "id": "e2", "source": "mean_1", "target": "sink_1" }
              ]
            }
            """);
        var config = ParseJson(
            """
            {
              "dataInputs": [
                { "nodeRef": "in_1", "paramIds": ["1001", "1002"] }
              ]
            }
            """);

        var result = await validator.ValidateAsync(reactFlow, config, CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains(result.Issues, i => i.Code == "DAG_011");
    }

    private static JsonElement SingleSourceConfig(string nodeRef, string paramId)
    {
        return ParseJson(
            $$"""
            {
              "dataInputs": [
                { "nodeRef": "{{nodeRef}}", "paramIds": ["{{paramId}}"] }
              ]
            }
            """);
    }

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
