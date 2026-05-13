using Microsoft.Extensions.Options;
using SatelliteData.Application.Assets;
using SatelliteData.Domain.Assets;
using System.Net.Http.Json;
using System.Text.Json;

namespace SatelliteData.Infrastructure.HttpClients;

public sealed class SatelliteAssetApiClient(HttpClient httpClient, IOptions<AssetProviderOptions> options) : ISatelliteAssetProvider
{
    private readonly AssetProviderOptions _options = options.Value;
    public async Task<IReadOnlyCollection<TestBatchCache>> GetTestPhasesAsync(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken)
    {
        var url = $"{_options.SatelliteAssetApiBaseUrl}/api/satellite-assets/satellites/{Uri.EscapeDataString(tasookNo)}/{Uri.EscapeDataString(satelliteNo)}/test-phases";

        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        return root.GetArrayItems("items", "datas", "testPhases", "phases", "testBatches")
            .Select(item => new TestBatchCache(
                tasookNo,
                satelliteNo,
                item.GetStringOrNull("phaseId", "testBatchId", "batchId", "id") ?? "",
                item.GetStringOrNull("phaseName", "scenario", "scene", "testScenario"),
                item.GetDateTimeOffsetOrNull("startTs", "start", "startTime") ?? DateTimeOffset.MinValue,
                item.GetDateTimeOffsetOrNull("endTs", "end", "endTime") ?? DateTimeOffset.MinValue,
                item.GetStringOrNull("sourceVersion", "version"),
                now,
                item.Clone()))
            .Where(item => !string.IsNullOrWhiteSpace(item.TestBatchId) && item.StartTs != DateTimeOffset.MinValue)
            .ToArray();
    }
}
