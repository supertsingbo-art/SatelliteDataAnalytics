using System.Text.Json;
using SatelliteData.Application.Templates;
using SatelliteData.Infrastructure.PostgreSql;
using Xunit;

namespace SatelliteData.UnitTests;

public sealed class AlgorithmTemplateValidatorTests
{
    [Fact]
    public async Task ValidateAsync_AcceptsSaveResultAsOutputNode()
    {
        var registry = new AlgorithmRegistryService(new InMemoryAlgorithmPackageRepository());
        var validator = new AlgorithmTemplateValidator(registry);
        var reactFlow = ParseJson(
            """
            {
              "nodes": [
                { "id": "src_1", "type": "source", "data": {} },
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
    public async Task ValidateAsync_SaveResultRequiresExactlyOneIncomingEdge()
    {
        var registry = new AlgorithmRegistryService(new InMemoryAlgorithmPackageRepository());
        var validator = new AlgorithmTemplateValidator(registry);
        var reactFlow = ParseJson(
            """
            {
              "nodes": [
                { "id": "src_1", "type": "source", "data": {} },
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

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
