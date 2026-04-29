using System.Net.Http.Json;
using System.Text.Json;
using SatelliteData.Application.Assets;
using SatelliteData.Domain.Assets;

namespace SatelliteData.Infrastructure.HttpClients;

public sealed class SatelliteAssetApiClient(HttpClient httpClient) : ISatelliteAssetProvider
{
    public Task<IReadOnlyCollection<SatelliteCache>> GetSatellitesAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyCollection<SatelliteCache>>(Array.Empty<SatelliteCache>());
    }

    public Task<IReadOnlyCollection<ParamCache>> GetParametersAsync(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyCollection<ParamCache>>(Array.Empty<ParamCache>());
    }

    public Task<MongoConnectionInfo?> GetMongoInfoAsync(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<MongoConnectionInfo?>(null);
    }

    public async Task<IReadOnlyCollection<TestBatchCache>> GetTestBatchesAsync(
        string tasookNo,
        string satelliteNo,
        DateTimeOffset? start,
        DateTimeOffset? end,
        CancellationToken cancellationToken)
    {
        var url = $"/api/satellite-assets/satellites/{Uri.EscapeDataString(tasookNo)}/{Uri.EscapeDataString(satelliteNo)}/test-batches";
        var query = new List<string>();
        if (start.HasValue)
        {
            query.Add($"start={Uri.EscapeDataString(start.Value.UtcDateTime.ToString("O"))}");
        }

        if (end.HasValue)
        {
            query.Add($"end={Uri.EscapeDataString(end.Value.UtcDateTime.ToString("O"))}");
        }

        if (query.Count > 0)
        {
            url += "?" + string.Join("&", query);
        }

        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        return root.GetArrayItems("items", "datas", "testBatches")
            .Select(item => new TestBatchCache(
                tasookNo,
                satelliteNo,
                item.GetStringOrNull("testBatchId", "batchId", "id") ?? "",
                item.GetStringOrNull("scenario", "scene", "testScenario"),
                item.GetDateTimeOffsetOrNull("startTs", "start", "startTime") ?? DateTimeOffset.MinValue,
                item.GetDateTimeOffsetOrNull("endTs", "end", "endTime") ?? DateTimeOffset.MinValue,
                item.GetStringOrNull("sourceVersion", "version"),
                now,
                item.Clone()))
            .Where(item => !string.IsNullOrWhiteSpace(item.TestBatchId) && item.StartTs != DateTimeOffset.MinValue)
            .ToArray();
    }
}
