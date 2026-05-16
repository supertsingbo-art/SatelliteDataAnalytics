using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SatelliteData.Application.Assets;
using SatelliteData.Domain.Assets;

namespace SatelliteData.Infrastructure.HttpClients;

/// <summary>
/// 卫星测试流程规划服务 HTTP 客户端（见 <c>doc/卫星测试流程规划服务API说明文档.docx</c>）。
/// </summary>
public sealed class SatelliteAssetApiClient(HttpClient httpClient, IOptions<AssetProviderOptions> options)
    : ISatelliteAssetProvider
{
    private readonly AssetProviderOptions _options = options.Value;

    public async Task<IReadOnlyCollection<TestBatchCache>> GetTestPhasesAsync(
        string tasookNo,
        string satelliteNo,
        string? dbStage,
        CancellationToken cancellationToken)
    {
        var baseUrl = _options.SatelliteAssetApiBaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/api/testplan/teststages";

        var requestBody = new
        {
            taskNo = tasookNo,
            satNo = satelliteNo,
            dbStage = string.IsNullOrWhiteSpace(dbStage) ? _options.DefaultDbStage : dbStage
        };

        using var response = await httpClient.PostAsJsonAsync(url, requestBody, cancellationToken);
        response.EnsureSuccessStatusCode();

        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        return root.GetArrayItems("datas", "items", "data", "testPhases", "phases", "testBatches")
            .Select(item => MapTestStage(tasookNo, satelliteNo, item, now))
            .Where(item => !string.IsNullOrWhiteSpace(item.TestBatchId) && item.StartTs != DateTimeOffset.MinValue)
            .ToArray();
    }

    private static TestBatchCache MapTestStage(
        string tasookNo,
        string satelliteNo,
        JsonElement item,
        DateTimeOffset syncedAt)
    {
        var stageName = item.GetStringOrNull(
            "teststagename",
            "testStageName",
            "test_stage_name",
            "phaseName",
            "scenario",
            "scene",
            "testScenario",
            "测试阶段");
        stageName = string.IsNullOrWhiteSpace(stageName) ? null : stageName.Trim();

        var start = item.GetDateTimeOffsetOrNull("fromdt", "fromDt", "from", "startTs", "start", "startTime")
            ?? DateTimeOffset.MinValue;
        var end = item.GetDateTimeOffsetOrNull("todt", "toDt", "to", "endTs", "end", "endTime")
            ?? DateTimeOffset.MinValue;

        var batchId = item.GetStringOrNull("phaseId", "testBatchId", "batchId", "id")
            ?? stageName
            ?? "";

        return new TestBatchCache(
            tasookNo,
            satelliteNo,
            batchId.Trim(),
            stageName,
            start,
            end,
            item.GetStringOrNull("sourceVersion", "version"),
            syncedAt,
            item.Clone());
    }
}
