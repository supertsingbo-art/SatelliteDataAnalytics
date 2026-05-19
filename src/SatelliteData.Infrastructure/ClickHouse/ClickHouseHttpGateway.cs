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

    public async Task<IReadOnlyList<HqParamPointRow>> QueryHqParamPointsAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        IReadOnlyList<string> paramIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        int maxRows,
        CancellationToken cancellationToken)
    {
        if (paramIds.Count == 0) return [];

        var ws = windowStart.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var we = windowEnd.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var inList = string.Join(", ", paramIds.Select(id => $"'{Escape(id)}'"));
        var limit = Math.Clamp(maxRows, 1, 50_000);
        var sql = $"""
            SELECT param_id, ts, processed_value, is_outlier
            FROM hq_param_point
            WHERE tasook_no = '{Escape(tasookNo)}'
              AND satellite_no = '{Escape(satelliteNo)}'
              AND test_batch_id = '{Escape(testBatchId)}'
              AND param_id IN ({inList})
              AND ts >= parseDateTime64BestEffort('{ws}')
              AND ts <= parseDateTime64BestEffort('{we}')
            ORDER BY ts, param_id
            LIMIT {limit}
            FORMAT JSONEachRow
            """;

        using var req = new HttpRequestMessage(HttpMethod.Post, _baseUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authValue);
        req.Content = new StringContent(sql, Encoding.UTF8, "text/plain");
        var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("ClickHouse matrix query failed {Status}: {Body}", resp.StatusCode, text);
            return [];
        }

        var list = new List<HqParamPointRow>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("param_id", out var pidEl)) continue;
            var pid = pidEl.GetString();
            if (string.IsNullOrEmpty(pid)) continue;
            if (!root.TryGetProperty("ts", out var tsEl)) continue;
            var tsStr = tsEl.GetString();
            if (string.IsNullOrEmpty(tsStr)) continue;
            if (!DateTimeOffset.TryParse(tsStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var ts))
            {
                continue;
            }

            if (!root.TryGetProperty("processed_value", out var vEl) || vEl.ValueKind == JsonValueKind.Null) continue;
            if (!vEl.TryGetDouble(out var v)) continue;
            var isOutlier = root.TryGetProperty("is_outlier", out var oEl) && oEl.ValueKind == JsonValueKind.Number
                && oEl.TryGetUInt32(out var o)
                && o != 0;
            list.Add(new HqParamPointRow(pid, ts, v, isOutlier));
        }

        return list;
    }

    public Task<long> CountDistinctTimestampsAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        IReadOnlyList<string> paramIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken) =>
        ExecuteScalarLongAsync(
            BuildMatrixFilterSql(
                tasookNo,
                satelliteNo,
                testBatchId,
                paramIds,
                windowStart,
                windowEnd,
                "SELECT count() AS cnt FROM (SELECT ts FROM hq_param_point WHERE {filter} GROUP BY ts)"),
            cancellationToken);

    public Task<IReadOnlyList<HqParamPointRow>> QueryHqParamPointsByTimestampPageAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        IReadOnlyList<string> paramIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (paramIds.Count == 0) return Task.FromResult<IReadOnlyList<HqParamPointRow>>([]);

        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, 200);
        var offset = (safePage - 1) * safePageSize;
        var filter = BuildMatrixWhereClause(tasookNo, satelliteNo, testBatchId, paramIds, windowStart, windowEnd);
        var sql = $"""
            SELECT param_id, ts, processed_value, is_outlier
            FROM hq_param_point
            WHERE {filter}
              AND ts IN (
                SELECT ts FROM (
                  SELECT ts
                  FROM hq_param_point
                  WHERE {filter}
                  GROUP BY ts
                  ORDER BY ts ASC
                  LIMIT {safePageSize} OFFSET {offset}
                )
              )
            ORDER BY ts ASC, param_id ASC
            FORMAT JSONEachRow
            """;

        return QueryHqParamPointsFromSqlAsync(sql, cancellationToken);
    }

    private static string BuildMatrixWhereClause(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        IReadOnlyList<string> paramIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd)
    {
        var ws = windowStart.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var we = windowEnd.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var inList = string.Join(", ", paramIds.Select(id => $"'{Escape(id)}'"));
        return $"""
            tasook_no = '{Escape(tasookNo)}'
              AND satellite_no = '{Escape(satelliteNo)}'
              AND test_batch_id = '{Escape(testBatchId)}'
              AND param_id IN ({inList})
              AND ts >= parseDateTime64BestEffort('{ws}')
              AND ts <= parseDateTime64BestEffort('{we}')
            """;
    }

    private static string BuildMatrixFilterSql(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        IReadOnlyList<string> paramIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        string sqlTemplate)
    {
        if (paramIds.Count == 0) return "SELECT 0 AS cnt";
        var filter = BuildMatrixWhereClause(tasookNo, satelliteNo, testBatchId, paramIds, windowStart, windowEnd);
        return sqlTemplate.Replace("{filter}", filter, StringComparison.Ordinal);
    }

    private async Task<long> ExecuteScalarLongAsync(string sql, CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, _baseUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authValue);
        req.Content = new StringContent(sql + "\nFORMAT JSONEachRow", Encoding.UTF8, "text/plain");
        var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("ClickHouse scalar query failed {Status}: {Body}", resp.StatusCode, text);
            return 0;
        }

        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("cnt", out var cntEl) && cntEl.TryGetInt64(out var cnt))
            {
                return cnt;
            }
        }

        return 0;
    }

    private async Task<IReadOnlyList<HqParamPointRow>> QueryHqParamPointsFromSqlAsync(
        string sql,
        CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, _baseUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authValue);
        req.Content = new StringContent(sql, Encoding.UTF8, "text/plain");
        var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("ClickHouse matrix page query failed {Status}: {Body}", resp.StatusCode, text);
            return [];
        }

        var list = new List<HqParamPointRow>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("param_id", out var pidEl)) continue;
            var pid = pidEl.GetString();
            if (string.IsNullOrEmpty(pid)) continue;
            if (!root.TryGetProperty("ts", out var tsEl)) continue;
            var tsStr = tsEl.GetString();
            if (string.IsNullOrEmpty(tsStr)) continue;
            if (!DateTimeOffset.TryParse(tsStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var ts))
            {
                continue;
            }

            if (!root.TryGetProperty("processed_value", out var vEl) || vEl.ValueKind == JsonValueKind.Null) continue;
            if (!vEl.TryGetDouble(out var v)) continue;
            var isOutlier = root.TryGetProperty("is_outlier", out var oEl) && oEl.ValueKind == JsonValueKind.Number
                && oEl.TryGetUInt32(out var o)
                && o != 0;
            list.Add(new HqParamPointRow(pid, ts, v, isOutlier));
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
