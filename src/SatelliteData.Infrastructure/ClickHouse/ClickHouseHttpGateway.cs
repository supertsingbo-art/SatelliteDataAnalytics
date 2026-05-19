using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SatelliteData.Application.Pipeline;
using SatelliteData.Infrastructure;

namespace SatelliteData.Infrastructure.ClickHouse;

public sealed class ClickHouseHttpGateway : IClickHouseGateway
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly ILogger<ClickHouseHttpGateway> _logger;
    private readonly Uri _baseUri;
    private readonly string _authValue;

    public ClickHouseHttpGateway(
        IOptions<DatabaseConnectionOptions> options,
        ILogger<ClickHouseHttpGateway> logger)
    {
        _logger = logger;
        var cs = options.Value.ClickHouse;
        ParseConnectionString(cs, out var host, out var port, out var user, out var password);
        _baseUri = new Uri($"http://{host}:{port}/");
        _authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));
    }

    public async Task ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, _baseUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authValue);
        req.Content = new StringContent(sql, Encoding.UTF8, "text/plain");
        var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("ClickHouse error {Status}: {Body}", resp.StatusCode, body);
            throw new InvalidOperationException($"ClickHouse 执行失败：{resp.StatusCode} {body}");
        }
    }

    public Task EnsureHqParamPointTableAsync(CancellationToken cancellationToken) =>
        ExecuteNonQueryAsync(
            """
            CREATE TABLE IF NOT EXISTS hq_param_point
            (
                tasook_no LowCardinality(String),
                satellite_no LowCardinality(String),
                test_batch_id LowCardinality(String),
                param_id LowCardinality(String),
                ts DateTime64(3, 'UTC'),
                raw_value Nullable(Float64),
                processed_value Nullable(Float64),
                is_outlier UInt8,
                version UInt64,
                ingested_at DateTime64(3, 'UTC') DEFAULT now64(3)
            )
            ENGINE = ReplacingMergeTree(version)
            PARTITION BY (toYYYYMM(ts), tasook_no, satellite_no)
            ORDER BY (tasook_no, satellite_no, test_batch_id, param_id, ts)
            """,
            cancellationToken);

    public async Task InsertJsonEachRowAsync(string tableName, IReadOnlyList<string> jsonRows, CancellationToken cancellationToken)
    {
        if (jsonRows.Count == 0) return;
        var sql = $"INSERT INTO {tableName} FORMAT JSONEachRow\n";
        var sb = new StringBuilder(sql);
        foreach (var row in jsonRows)
        {
            sb.AppendLine(row);
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, _baseUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authValue);
        req.Content = new StringContent(sb.ToString(), Encoding.UTF8, "text/plain");
        var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("ClickHouse insert error {Status}: {Body}", resp.StatusCode, body);
            throw new InvalidOperationException($"ClickHouse 写入失败：{resp.StatusCode} {body}");
        }
    }

    public async Task<IReadOnlyList<(DateTimeOffset Ts, double Value)>> QueryProcessedSeriesAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        string paramId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken)
    {
        var ws = windowStart.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var we = windowEnd.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var sql = $"""
            SELECT ts, processed_value
            FROM hq_param_point
            WHERE tasook_no = '{Escape(tasookNo)}'
              AND satellite_no = '{Escape(satelliteNo)}'
              AND test_batch_id = '{Escape(testBatchId)}'
              AND param_id = '{Escape(paramId)}'
              AND ts >= parseDateTime64BestEffort('{ws}')
              AND ts <= parseDateTime64BestEffort('{we}')
            ORDER BY ts
            FORMAT JSONEachRow
            """;

        using var req = new HttpRequestMessage(HttpMethod.Post, _baseUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authValue);
        req.Content = new StringContent(sql, Encoding.UTF8, "text/plain");
        var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("ClickHouse query failed {Status}: {Body}", resp.StatusCode, text);
            return Array.Empty<(DateTimeOffset, double)>();
        }

        var list = new List<(DateTimeOffset Ts, double Value)>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("ts", out var tsEl)) continue;
            var tsStr = tsEl.GetString();
            if (string.IsNullOrEmpty(tsStr)) continue;
            if (!DateTimeOffset.TryParse(tsStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var ts))
            {
                continue;
            }

            if (!root.TryGetProperty("processed_value", out var vEl) || vEl.ValueKind == JsonValueKind.Null) continue;
            if (!vEl.TryGetDouble(out var v)) continue;
            list.Add((ts, v));
        }

        return list;
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");

    private static void ParseConnectionString(string cs, out string host, out int port, out string user, out string password)
    {
        host = "localhost";
        port = 8123;
        user = "default";
        password = "";
        foreach (var part in cs.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0) continue;
            var key = part[..idx].Trim();
            var val = part[(idx + 1)..].Trim();
            if (string.Equals(key, "Host", StringComparison.OrdinalIgnoreCase)) host = val;
            else if (string.Equals(key, "Port", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, out var p)) port = p;
            else if (string.Equals(key, "Username", StringComparison.OrdinalIgnoreCase)) user = val;
            else if (string.Equals(key, "Password", StringComparison.OrdinalIgnoreCase)) password = val;
        }
    }
}
