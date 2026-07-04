using System.Text.Json;
using SatelliteData.Application.Templates;
using Xunit;

namespace SatelliteData.UnitTests;

public sealed class AlgorithmTemplateConfigParserTests
{
    [Fact]
    public void TryResolveSourceParamId_LegacyParamIdInReactFlowData()
    {
        var nodeData = ParseJson("""{"paramId": "legacy_param"}""");
        var config = ParseJson("{}");

        var ok = AlgorithmTemplateConfigParser.TryResolveSourceParamId(
            nodeData,
            "src_1",
            config,
            out var paramId,
            out var failure);

        Assert.True(ok);
        Assert.Equal("legacy_param", paramId);
        Assert.Equal(SourceParamResolveFailure.None, failure);
    }

    [Fact]
    public void TryResolveSourceParamId_ConfigJsonSingleParam()
    {
        var nodeData = ParseJson("""{"nodeRef": "in_abc"}""");
        var config = ParseJson(
            """
            {
              "dataInputs": [
                {
                  "nodeRef": "in_abc",
                  "sourceTable": "hq_param_point",
                  "paramIds": ["1001"],
                  "valueField": "processed_value",
                  "includeOutliers": false,
                  "outputName": "series_in_abc"
                }
              ]
            }
            """);

        var ok = AlgorithmTemplateConfigParser.TryResolveSourceParamId(
            nodeData,
            "src_xyz",
            config,
            out var paramId,
            out var failure);

        Assert.True(ok);
        Assert.Equal("1001", paramId);
        Assert.Equal(SourceParamResolveFailure.None, failure);
    }

    [Fact]
    public void TryResolveSourceParamId_FallsBackToNodeIdWhenNodeRefMissing()
    {
        var nodeData = ParseJson("{}");
        var config = ParseJson(
            """
            {
              "dataInputs": [
                {
                  "nodeRef": "src_1",
                  "paramIds": ["2002"]
                }
              ]
            }
            """);

        var ok = AlgorithmTemplateConfigParser.TryResolveSourceParamId(
            nodeData,
            "src_1",
            config,
            out var paramId,
            out var failure);

        Assert.True(ok);
        Assert.Equal("2002", paramId);
        Assert.Equal(SourceParamResolveFailure.None, failure);
    }

    [Fact]
    public void TryResolveSourceParamId_MissingParamIds()
    {
        var nodeData = ParseJson("""{"nodeRef": "in_abc"}""");
        var config = ParseJson(
            """
            {
              "dataInputs": [
                { "nodeRef": "in_abc", "paramIds": [] }
              ]
            }
            """);

        var ok = AlgorithmTemplateConfigParser.TryResolveSourceParamId(
            nodeData,
            "src_1",
            config,
            out _,
            out var failure);

        Assert.False(ok);
        Assert.Equal(SourceParamResolveFailure.Missing, failure);
    }

    [Fact]
    public void TryResolveSourceParamId_MultipleParamIds()
    {
        var nodeData = ParseJson("""{"nodeRef": "in_abc"}""");
        var config = ParseJson(
            """
            {
              "dataInputs": [
                { "nodeRef": "in_abc", "paramIds": ["1001", "1002"] }
              ]
            }
            """);

        var ok = AlgorithmTemplateConfigParser.TryResolveSourceParamId(
            nodeData,
            "src_1",
            config,
            out _,
            out var failure);

        Assert.False(ok);
        Assert.Equal(SourceParamResolveFailure.Multiple, failure);
    }

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
