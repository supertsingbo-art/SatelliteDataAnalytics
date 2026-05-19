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
        using var response = await httpClient.GetAsync($"{_options.MassDataApiBaseUrl}/api/mass-data/satellites", cancellationToken);
        response.EnsureSuccessStatusCode();

        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        return root.GetArrayItems("items", "datas", "satellites")
            .Select(item =>
            {
                var tasookNo = item.GetStringOrNull("tasookNo", "taskNo", "taskno") ?? "";
                var tasookName = item.GetStringOrNull("tasookName", "taskName", "taskname");
                var satelliteNo = item.GetStringOrNull("satelliteNo", "satNo", "satno") ?? "";
                var dbStage = item.GetStringOrNull("dbStage", "stage") ?? _options.DefaultDbStage;
                return new SatelliteCache(
                    tasookNo,
                    tasookName,
                    satelliteNo,
                    item.GetStringOrNull("satelliteName", "satName", "satname", "name") ?? satelliteNo,
                    item.GetStringOrNull("satelliteType", "satType", "type"),
                    dbStage,
                    null,
                    item.GetStringOrNull("sourceVersion", "version") ?? dbStage,
                    now,
                    CachedParameterCount: 0,
                    CachedCommandCount: 0,
                    IsEnabled: true,
                    item.Clone());
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.TasookNo) && !string.IsNullOrWhiteSpace(item.SatelliteNo))
            .ToArray();
    }

    public async Task<IReadOnlyCollection<ParamCache>> GetParametersAsync(
        string tasookNo,
        string satelliteNo,
        string? dbStage,
        CancellationToken cancellationToken)
    {
        var request = CreateLookupRequest(tasookNo, satelliteNo, dbStage);
        using var response = await httpClient.PostAsJsonAsync($"{_options.MassDataApiBaseUrl}/api/mass-data/basic/parameters", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var sourceVersion = root.GetStringOrNull("sourceVersion", "version", "dbStage") ?? dbStage ?? _options.DefaultDbStage;

        return root.GetArrayItems("datas", "items", "parameters", "paras")
            .Select(item => MassDataParameterMapper.Map(item, tasookNo, satelliteNo, sourceVersion, now))
            .Where(item => item is not null)
            .Cast<ParamCache>()
            .ToArray();
    }

    public async Task<IReadOnlyCollection<CommandCache>> GetCommandsAsync(
        string tasookNo,
        string satelliteNo,
        string? dbStage,
        CancellationToken cancellationToken)
    {
        var request = CreateLookupRequest(tasookNo, satelliteNo, dbStage);
        using var response = await httpClient.PostAsJsonAsync($"{_options.MassDataApiBaseUrl}/api/mass-data/basic/commands", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var sourceVersion = root.GetStringOrNull("sourceVersion", "version", "dbStage") ?? dbStage ?? _options.DefaultDbStage;

        return root.GetArrayItems("datas", "items", "commands", "cmds", "instructions")
            .Select(item => MassDataCommandMapper.Map(item, tasookNo, satelliteNo, sourceVersion, now))
            .Where(item => item is not null)
            .Cast<CommandCache>()
            .ToArray();
    }

    public async Task<MongoConnectionInfo?> GetMongoInfoAsync(
        string tasookNo,
        string satelliteNo,
        string? dbStage,
        CancellationToken cancellationToken)
    {
        var request = CreateLookupRequest(tasookNo, satelliteNo, dbStage);
        using var response = await httpClient.PostAsJsonAsync($"{_options.MassDataApiBaseUrl}/api/mass-data/satellite/config", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var rawUri = root.GetStringOrNull("mongoUri", "mongoQueryConn", "mongoqueryconn", "cfgConn");
        if (string.IsNullOrWhiteSpace(rawUri))
        {
            return null;
        }

        var sanitizedUri = MongoUriSanitizer.StripCredentials(rawUri);
        var dbName = root.GetStringOrNull("dbName", "mongoDbName")
                     ?? MongoUriSanitizer.ExtractDatabaseName(sanitizedUri)
                     ?? $"{tasookNo}_{satelliteNo}";

        return new MongoConnectionInfo(
            sanitizedUri,
            dbName,
            root.GetStringOrNull("authRef", "mongoAuthRef"));
    }

    private object CreateLookupRequest(string tasookNo, string satelliteNo, string? dbStage)
    {
        return new
        {
            taskNo = tasookNo,
            satNo = satelliteNo,
            dbStage = string.IsNullOrWhiteSpace(dbStage) ? _options.DefaultDbStage : dbStage
        };
    }
}
