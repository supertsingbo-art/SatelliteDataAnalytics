using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SatelliteData.Application.Assets;
using SatelliteData.Domain.Assets;

namespace SatelliteData.Infrastructure.HttpClients;

/// <summary>
/// 海量数据接口 Web API v2 客户端。v2 卫星主键为 <c>taskNo + satNo</c>。
/// 仅封装资产同步用到的 4 个接口：<c>satellites</c>、<c>basic/parameters</c>、<c>basic/commands</c>、<c>satellite/config</c>。
/// </summary>
public sealed class MassDataApiClient(HttpClient httpClient) : IMassDataAssetProvider
{
    private const string ApiV2Prefix = "/api/v2/mass-data";

    public async Task<IReadOnlyCollection<SatelliteCache>> GetSatellitesAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"{ApiV2Prefix}/satellites", cancellationToken);
        response.EnsureSuccessStatusCode();

        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        return root.GetArrayItems("datas", "items", "satellites")
            .Select(item =>
            {
                var tasookNo = item.GetStringOrNull("taskNo", "tasookNo", "taskno") ?? "";
                var tasookName = item.GetStringOrNull("taskName", "tasookName", "taskname");
                var satelliteNo = item.GetStringOrNull("satNo", "satelliteNo", "satno") ?? "";
                return new SatelliteCache(
                    tasookNo,
                    tasookName,
                    satelliteNo,
                    item.GetStringOrNull("satName", "satelliteName", "satname", "name") ?? satelliteNo,
                    item.GetStringOrNull("satType", "satelliteType", "type"),
                    null,
                    item.GetStringOrNull("sourceVersion", "version"),
                    now,
                    CachedParameterCount: 0,
                    CachedCommandCount: 0,
                    IsEnabled: item.GetBoolOrNull("enabled") ?? true,
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
        using var response = await httpClient.PostAsJsonAsync($"{ApiV2Prefix}/basic/parameters", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var sourceVersion = root.GetStringOrNull("sourceVersion", "version");

        return root.GetArrayItems("datas", "items", "parameters", "paras")
            .Select(item => MassDataParameterMapper.Map(item, tasookNo, satelliteNo, sourceVersion, now))
            .Where(item => item is not null)
            .Cast<ParamCache>()
            .ToArray();
    }

    public async Task<IReadOnlyCollection<CommandCache>> GetCommandsAsync(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken)
    {
        var request = CreateLookupRequest(tasookNo, satelliteNo);
        using var response = await httpClient.PostAsJsonAsync($"{ApiV2Prefix}/basic/commands", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var sourceVersion = root.GetStringOrNull("sourceVersion", "version");

        return root.GetArrayItems("datas", "items", "commands", "cmds", "instructions")
            .Select(item => MassDataCommandMapper.Map(item, tasookNo, satelliteNo, sourceVersion, now))
            .Where(item => item is not null)
            .Cast<CommandCache>()
            .ToArray();
    }

    public async Task<MongoConnectionInfo?> GetMongoInfoAsync(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken)
    {
        var request = CreateLookupRequest(tasookNo, satelliteNo);
        using var response = await httpClient.PostAsJsonAsync($"{ApiV2Prefix}/satellite/config", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var root = await ReadJsonRootAsync(response, cancellationToken);
        var rawUri = root.GetStringOrNull("mongoQueryConn", "basicConn", "mongoUri", "cfgConn");
        if (string.IsNullOrWhiteSpace(rawUri))
        {
            return null;
        }

        var sanitizedUri = MongoUriSanitizer.StripCredentials(rawUri);
        var dbName = root.GetStringOrNull("dbName", "mongoDbName", "database", "mongoDatabase")
                     ?? MongoUriSanitizer.ExtractDatabaseName(sanitizedUri)
                     ?? MongoUriSanitizer.ExtractDatabaseName(rawUri)
                     ?? $"{tasookNo}_{satelliteNo}";
        dbName = MongoUriSanitizer.NormalizeDbName(dbName);

        return new MongoConnectionInfo(
            sanitizedUri,
            dbName,
            root.GetStringOrNull("authRef", "mongoAuthRef"));
    }

    private static object CreateLookupRequest(string tasookNo, string satelliteNo) =>
        new { taskNo = tasookNo, satNo = satelliteNo };

    private static async Task<JsonElement> ReadJsonRootAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var json = Encoding.UTF8.GetString(bytes);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
