using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SatelliteData.Application.Pipeline;

namespace SatelliteData.Infrastructure.HttpClients;

public sealed class MassDataConditionHistoryProvider(
    HttpClient httpClient,
    IOptions<AssetProviderOptions> options) : IConditionHistoryProvider
{
    private readonly AssetProviderOptions _options = options.Value;
    private const string ApiV2Prefix = "/api/v2/mass-data";

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<RawSeriesPoint>>> QueryParameterSeriesAsync(
        string tasookNo,
        string satelliteNo,
        string? dbStage,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        IReadOnlyCollection<ParameterHistoryLookup> lookups,
        CancellationToken cancellationToken)
    {
        var result = lookups.ToDictionary(
            x => x.ParamId,
            _ => (IReadOnlyList<RawSeriesPoint>)Array.Empty<RawSeriesPoint>(),
            StringComparer.Ordinal);
        if (lookups.Count == 0)
        {
            return result;
        }

        var request = new
        {
            taskNo = tasookNo,
            satNo = satelliteNo,
            dbStage = string.IsNullOrWhiteSpace(dbStage) ? _options.DefaultDbStage : dbStage,
            fromDt = windowStart.UtcDateTime.ToString("O"),
            toDt = windowEnd.UtcDateTime.ToString("O"),
            pkgParaIds = lookups.Select(x => new
            {
                pid = x.PrmSysId,
                id = x.ParaId,
                rtDelayFlag = 0,
                dataProvider = 0
            }).ToArray()
        };
        using var response = await httpClient.PostAsJsonAsync(
            $"{ApiV2Prefix}/query/parameters",
            request,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var rows = root.GetArrayItems("datas", "items");
        var lookupByParaId = lookups.ToDictionary(x => x.ParaId, x => x, EqualityComparer<int>.Default);
        var grouped = new Dictionary<string, List<RawSeriesPoint>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var paraId = row.GetIntOrNull("id", "paramId");
            if (paraId is null || !lookupByParaId.TryGetValue(paraId.Value, out var lookup))
            {
                continue;
            }

            var dt = row.GetDateTimeOffsetOrNull("dt", "ts");
            if (dt is null)
            {
                continue;
            }

            var rawValue = row.GetStringOrNull("pv", "value", "processed_value");
            if (string.IsNullOrWhiteSpace(rawValue)
                || !double.TryParse(rawValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            {
                var n = row.GetDoubleOrNull("pv", "value", "processed_value");
                if (n is null)
                {
                    continue;
                }

                value = n.Value;
            }

            if (!grouped.TryGetValue(lookup.ParamId, out var list))
            {
                list = new List<RawSeriesPoint>();
                grouped[lookup.ParamId] = list;
            }

            list.Add(new RawSeriesPoint(dt.Value, value));
        }

        foreach (var pair in grouped)
        {
            result[pair.Key] = pair.Value
                .OrderBy(x => x.Ts)
                .ToArray();
        }

        return result;
    }

    public async Task<IReadOnlyList<InstructionHistoryPoint>> QueryInstructionHistoryAsync(
        string tasookNo,
        string satelliteNo,
        string? dbStage,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        IReadOnlyCollection<InstructionHistoryLookup> lookups,
        CancellationToken cancellationToken)
    {
        if (lookups.Count == 0)
        {
            return Array.Empty<InstructionHistoryPoint>();
        }

        var request = new
        {
            taskNo = tasookNo,
            satNo = satelliteNo,
            dbStage = string.IsNullOrWhiteSpace(dbStage) ? _options.DefaultDbStage : dbStage,
            fromDt = windowStart.UtcDateTime.ToString("O"),
            toDt = windowEnd.UtcDateTime.ToString("O"),
            instIds = lookups.Select(x => new object[] { x.CmdId, x.ChannelId }).ToArray()
        };
        using var response = await httpClient.PostAsJsonAsync(
            $"{ApiV2Prefix}/query/instructions",
            request,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var rows = root.GetArrayItems("datas", "items");
        var lookupByCmdId = lookups.ToDictionary(x => x.CmdId, x => x, EqualityComparer<int>.Default);
        var result = new List<InstructionHistoryPoint>();
        foreach (var row in rows)
        {
            var cmdId = row.GetIntOrNull("ci", "cmdId");
            var ts = row.GetDateTimeOffsetOrNull("et", "executeTime", "ts");
            if (cmdId is null || ts is null || !lookupByCmdId.TryGetValue(cmdId.Value, out var lookup))
            {
                continue;
            }

            result.Add(new InstructionHistoryPoint(
                lookup.CommandId,
                cmdId.Value,
                lookup.ChannelId,
                ts.Value));
        }

        return result
            .OrderBy(x => x.ExecuteTime)
            .ToArray();
    }
}
