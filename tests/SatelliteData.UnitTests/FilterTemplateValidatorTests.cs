using System.Text.Json;
using SatelliteData.Application.Templates;
using Xunit;

namespace SatelliteData.UnitTests;

public class FilterTemplateValidatorTests
{
    [Fact]
    public void Validate_AllowsConditionConfigWithoutRuleTree()
    {
        var config = JsonDocument.Parse(
            """
            {
              "scope": {
                "groupId": "d95de6dd-a5b1-4e70-b957-796cb47008dc",
                "referenceTasookNo": "TASK_A",
                "referenceSatelliteNo": "SAT_01"
              },
              "timeWindow": {
                "mode": "TEST_BATCH",
                "bufferBeforeSeconds": 5,
                "bufferAfterSeconds": 5
              },
              "conditionConfig": {
                "instructions": {
                  "startRelation": "OR",
                  "endRelation": "OR",
                  "startCommands": [
                    { "conditionId": "S1", "commandId": "1001", "channelId": 0 }
                  ]
                },
                "parameters": [
                  { "conditionId": "P1", "paramId": "2001", "operator": "=", "value": 10 }
                ],
                "expression": "P1"
              },
              "durationSeconds": 10,
              "targetParams": [
                { "paramId": "2001", "outlier": { "method": "SIGMA", "sigma": 3 } }
              ]
            }
            """).RootElement;

        FilterTemplateValidator.Validate(config);
    }

    [Fact]
    public void Validate_RejectsExpressionWithUnknownConditionId()
    {
        var config = JsonDocument.Parse(
            """
            {
              "scope": {
                "groupId": "d95de6dd-a5b1-4e70-b957-796cb47008dc",
                "referenceTasookNo": "TASK_A",
                "referenceSatelliteNo": "SAT_01"
              },
              "timeWindow": {
                "mode": "TEST_BATCH"
              },
              "conditionConfig": {
                "parameters": [
                  { "conditionId": "P1", "paramId": "2001", "operator": ">", "value": 0 }
                ],
                "expression": "P2 && P1"
              },
              "targetParams": [
                { "paramId": "2001", "outlier": { "method": "SIGMA", "sigma": 3 } }
              ]
            }
            """).RootElement;

        var ex = Assert.Throws<TemplateGovernanceException>(() => FilterTemplateValidator.Validate(config));
        Assert.Equal(TemplateErrorCodes.FilterTemplateConfigInvalid, ex.ErrorCode);
        Assert.Contains("未定义条件ID", ex.Message);
    }
}
