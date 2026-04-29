using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SatelliteData.Application.Assets;
using SatelliteData.Domain.Assets;

namespace SatelliteData.Infrastructure.HttpClients;

public sealed class MassDataApiClient(HttpClient httpClient, IOptions<AssetProviderOptions> options) : IMassDataAssetProvider
{
    private readonly AssetProviderOptions _options = options.Value;

    public async Task<IReadOnlyCollection<SatelliteCache>> GetSatellitesAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("/api/mass-data/satellites", cancellationToken);
        response.EnsureSuccessStatusCode();

        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        return root.GetArrayItems("items", "datas", "satellites")
            .Select(item =>
            {
                var tasookNo = item.GetStringOrNull("tasookNo", "taskNo", "taskno") ?? "";
                var satelliteNo = item.GetStringOrNull("satelliteNo", "satNo", "satno") ?? "";
                return new SatelliteCache(
                    tasookNo,
                    satelliteNo,
                    item.GetStringOrNull("satelliteName", "satName", "satname", "name") ?? satelliteNo,
                    item.GetStringOrNull("satelliteType", "satType", "type"),
                    null,
                    item.GetStringOrNull("sourceVersion", "version", "dbStage") ?? _options.DefaultDbStage,
                    now,
                    item.Clone());
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.TasookNo) && !string.IsNullOrWhiteSpace(item.SatelliteNo))
            .ToArray();
    }

    public async Task<IReadOnlyCollection<ParamCache>> GetParametersAsync(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken)
    {
        var request = CreateLookupRequest(tasookNo, satelliteNo);
        using var response = await httpClient.PostAsJsonAsync("/api/mass-data/basic/parameters", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var sourceVersion = root.GetStringOrNull("sourceVersion", "version", "dbStage") ?? _options.DefaultDbStage;

        return root.GetArrayItems("items", "datas", "parameters", "paras")
            .Select(item =>
            {
                var paramId = item.GetStringOrNull("paramId", "id", "paraId", "parameterId", "pc") ?? "";
                return new ParamCache(
                    tasookNo,
                    satelliteNo,
                    paramId,
                    item.GetStringOrNull("paramName", "name", "paraName", "parameterName", "pc") ?? paramId,
                    item.GetStringOrNull("unit", "unitName"),
                    item.GetStringOrNull("valueType", "dataType", "type") ?? "DOUBLE",
                    item.GetDoubleOrNull("valueMin", "min", "minValue"),
                    item.GetDoubleOrNull("valueMax", "max", "maxValue"),
                    sourceVersion,
                    now,
                    item.Clone());
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.ParamId))
            .ToArray();
    }

    public async Task<MongoConnectionInfo?> GetMongoInfoAsync(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken)
    {
        var request = CreateLookupRequest(tasookNo, satelliteNo);
        using var response = await httpClient.PostAsJsonAsync("/api/mass-data/satellite/config", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var mongoUri = root.GetStringOrNull("mongoUri", "mongoQueryConn", "mongoqueryconn", "cfgConn");
        if (string.IsNullOrWhiteSpace(mongoUri))
        {
            return null;
        }

        var dbName = TryGetDatabaseName(mongoUri) ?? $"{tasookNo}_{satelliteNo}";
        return new MongoConnectionInfo(
            mongoUri,
            root.GetStringOrNull("dbName", "mongoDbName") ?? dbName,
            root.GetStringOrNull("authRef", "mongoAuthRef"));
    }

    public Task<IReadOnlyCollection<TestBatchCache>> GetTestBatchesAsync(
        string tasookNo,
        string satelliteNo,
        DateTimeOffset? start,
        DateTimeOffset? end,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyCollection<TestBatchCache>>(Array.Empty<TestBatchCache>());
    }

    private object CreateLookupRequest(string tasookNo, string satelliteNo)
    {
        return new
        {
            taskNo = tasookNo,
            satNo = satelliteNo,
            dbStage = _options.DefaultDbStage
        };
    }

    private static string? TryGetDatabaseName(string mongoUri)
    {
        if (!Uri.TryCreate(mongoUri, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var db = uri.AbsolutePath.Trim('/');
        return string.IsNullOrWhiteSpace(db) ? null : db;
    }
}
