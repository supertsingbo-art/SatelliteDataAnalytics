using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SatelliteData.Application.Pipeline;
using SatelliteData.Application.Tasks;
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

    public async Task EnsureHqParamPointTableAsync(CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
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
                is_confirmed_outlier UInt8 DEFAULT 0,
                version UInt64,
                ingested_at DateTime64(3, 'UTC') DEFAULT now64(3)
            )
            ENGINE = ReplacingMergeTree(version)
            PARTITION BY (toYYYYMM(ts), tasook_no, satellite_no)
            ORDER BY (tasook_no, satellite_no, test_batch_id, param_id, ts)
            """,
            cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(
            "ALTER TABLE hq_param_point ADD COLUMN IF NOT EXISTS is_confirmed_outlier UInt8 DEFAULT 0",
            cancellationToken).ConfigureAwait(false);
    }

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
        var ws = ToUtcTimestampLiteral(windowStart);
        var we = ToUtcTimestampLiteral(windowEnd);
        var sql = $"""
            SELECT ts, argMax(processed_value, version) AS processed_value
            FROM hq_param_point
            WHERE tasook_no = '{Escape(tasookNo)}'
              AND satellite_no = '{Escape(satelliteNo)}'
              AND test_batch_id = '{Escape(testBatchId)}'
              AND param_id = '{Escape(paramId)}'
              AND ts >= parseDateTime64BestEffort('{ws}')
              AND ts <= parseDateTime64BestEffort('{we}')
            GROUP BY ts
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

        var ws = ToUtcTimestampLiteral(windowStart);
        var we = ToUtcTimestampLiteral(windowEnd);
        var inList = string.Join(", ", paramIds.Select(id => $"'{Escape(id)}'"));
        var limit = Math.Clamp(maxRows, 1, 50_000);
        var sql = $"""
            SELECT
              param_id,
              ts,
              argMax(processed_value, version) AS processed_value,
              argMax(is_outlier, version) AS is_outlier,
              argMax(is_confirmed_outlier, version) AS is_confirmed_outlier
            FROM hq_param_point
            WHERE tasook_no = '{Escape(tasookNo)}'
              AND satellite_no = '{Escape(satelliteNo)}'
              AND test_batch_id = '{Escape(testBatchId)}'
              AND param_id IN ({inList})
              AND ts >= parseDateTime64BestEffort('{ws}')
              AND ts <= parseDateTime64BestEffort('{we}')
            GROUP BY param_id, ts
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
            list.Add(ParseHqParamPointRow(root, pid, ts, v));
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
            SELECT
              param_id,
              ts,
              argMax(processed_value, version) AS processed_value,
              argMax(is_outlier, version) AS is_outlier,
              argMax(is_confirmed_outlier, version) AS is_confirmed_outlier
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
            GROUP BY param_id, ts
            ORDER BY ts ASC, param_id ASC
            FORMAT JSONEachRow
            """;

        return QueryHqParamPointsFromSqlAsync(sql, cancellationToken);
    }

    public Task<long> CountOutlierPointsAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        IReadOnlyList<string> paramIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        string? paramIdFilter,
        CancellationToken cancellationToken)
    {
        if (paramIds.Count == 0)
        {
            return Task.FromResult(0L);
        }

        var filter = BuildMatrixWhereClause(tasookNo, satelliteNo, testBatchId, paramIds, windowStart, windowEnd);
        var paramFilter = string.IsNullOrWhiteSpace(paramIdFilter)
            ? string.Empty
            : $" AND param_id = '{Escape(paramIdFilter.Trim())}'";
        var sql = $"""
            SELECT count() AS cnt
            FROM (
              SELECT param_id, ts
              FROM hq_param_point
              WHERE {filter}{paramFilter}
              GROUP BY param_id, ts
              HAVING argMax(is_outlier, version) = 1
            )
            """;
        return ExecuteScalarLongAsync(sql, cancellationToken);
    }

    public Task<IReadOnlyList<HqParamPointRow>> QueryOutlierPointsPageAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        IReadOnlyList<string> paramIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        string? paramIdFilter,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (paramIds.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<HqParamPointRow>>([]);
        }

        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, 200);
        var offset = (safePage - 1) * safePageSize;
        var filter = BuildOutlierWhereClause(
            tasookNo,
            satelliteNo,
            testBatchId,
            paramIds,
            windowStart,
            windowEnd,
            paramIdFilter);
        var sql = $"""
            SELECT
              param_id,
              ts,
              argMax(processed_value, version) AS processed_value,
              argMax(is_outlier, version) AS is_outlier,
              argMax(is_confirmed_outlier, version) AS is_confirmed_outlier
            FROM hq_param_point
            WHERE {filter}
            GROUP BY param_id, ts
            HAVING is_outlier = 1
            ORDER BY ts ASC, param_id ASC
            LIMIT {safePageSize} OFFSET {offset}
            FORMAT JSONEachRow
            """;

        return QueryHqParamPointsFromSqlAsync(sql, cancellationToken);
    }

    public async Task<HqParamPointInsertRow?> QueryLatestPointAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        string paramId,
        DateTimeOffset ts,
        CancellationToken cancellationToken)
    {
        var tsStr = ToUtcTimestampLiteral(ts);
        var sql = $"""
            SELECT tasook_no, satellite_no, test_batch_id, param_id, ts, raw_value, processed_value,
                   is_outlier, is_confirmed_outlier, version
            FROM hq_param_point
            WHERE tasook_no = '{Escape(tasookNo)}'
              AND satellite_no = '{Escape(satelliteNo)}'
              AND test_batch_id = '{Escape(testBatchId)}'
              AND param_id = '{Escape(paramId)}'
              AND ts = parseDateTime64BestEffort('{tsStr}')
            ORDER BY version DESC
            LIMIT 1
            FORMAT JSONEachRow
            """;

        using var req = new HttpRequestMessage(HttpMethod.Post, _baseUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authValue);
        req.Content = new StringContent(sql, Encoding.UTF8, "text/plain");
        var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (string.IsNullOrEmpty(line))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        if (!root.TryGetProperty("version", out var verEl) || !verEl.TryGetUInt64(out var version))
        {
            return null;
        }

        double? raw = null;
        if (root.TryGetProperty("raw_value", out var rawEl) && rawEl.ValueKind != JsonValueKind.Null
            && rawEl.TryGetDouble(out var rv))
        {
            raw = rv;
        }

        double? proc = null;
        if (root.TryGetProperty("processed_value", out var procEl) && procEl.ValueKind != JsonValueKind.Null
            && procEl.TryGetDouble(out var pv))
        {
            proc = pv;
        }

        var isOutlier = root.TryGetProperty("is_outlier", out var oEl) && oEl.TryGetUInt32(out var o) ? (byte)o : (byte)0;
        var isConfirmed = root.TryGetProperty("is_confirmed_outlier", out var cEl) && cEl.TryGetUInt32(out var c)
            ? (byte)c
            : (byte)0;

        return new HqParamPointInsertRow(
            tasookNo,
            satelliteNo,
            testBatchId,
            paramId,
            ts,
            raw,
            proc,
            isOutlier,
            isConfirmed,
            version);
    }

    public async Task InsertReviewedPointVersionsAsync(
        IReadOnlyList<HqParamPointInsertRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return;
        var jsonRows = rows.Select(r => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["tasook_no"] = r.TasookNo,
            ["satellite_no"] = r.SatelliteNo,
            ["test_batch_id"] = r.TestBatchId,
            ["param_id"] = r.ParamId,
            ["ts"] = ToUtcTimestampLiteral(r.Ts),
            ["raw_value"] = r.RawValue,
            ["processed_value"] = r.ProcessedValue,
            ["is_outlier"] = r.IsOutlier,
            ["is_confirmed_outlier"] = r.IsConfirmedOutlier,
            ["version"] = r.Version
        })).ToList();
        await InsertJsonEachRowAsync("hq_param_point", jsonRows, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteByClaimsAsync(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        IReadOnlyList<PreprocessParamClaimRequest> claims,
        ulong keepVersionFromInclusive,
        CancellationToken cancellationToken)
    {
        if (claims.Count == 0)
        {
            return;
        }

        foreach (var claim in claims)
        {
            if (string.IsNullOrWhiteSpace(claim.ParamId) || claim.SegmentStart >= claim.SegmentEnd)
            {
                continue;
            }

            var start = ToUtcTimestampLiteral(claim.SegmentStart);
            var end = ToUtcTimestampLiteral(claim.SegmentEnd);
            var sql = $"""
                ALTER TABLE hq_param_point DELETE
                WHERE tasook_no = '{Escape(tasookNo)}'
                  AND satellite_no = '{Escape(satelliteNo)}'
                  AND test_batch_id = '{Escape(testBatchId)}'
                  AND param_id = '{Escape(claim.ParamId.Trim())}'
                  AND ts >= parseDateTime64BestEffort('{start}')
                  AND ts < parseDateTime64BestEffort('{end}')
                  AND version < {keepVersionFromInclusive}
                """;
            await ExecuteNonQueryAsync(sql, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string BuildMatrixWhereClause(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        IReadOnlyList<string> paramIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd)
    {
        var ws = ToUtcTimestampLiteral(windowStart);
        var we = ToUtcTimestampLiteral(windowEnd);
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

    private static string BuildOutlierWhereClause(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        IReadOnlyList<string> paramIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        string? paramIdFilter)
    {
        var baseFilter = BuildMatrixWhereClause(tasookNo, satelliteNo, testBatchId, paramIds, windowStart, windowEnd);
        var outlierFilter = baseFilter;
        if (!string.IsNullOrWhiteSpace(paramIdFilter))
        {
            outlierFilter += $"\n  AND param_id = '{Escape(paramIdFilter.Trim())}'";
        }

        return outlierFilter;
    }

    private static string BuildOutlierFilterSql(
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        IReadOnlyList<string> paramIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        string? paramIdFilter,
        string sqlTemplate)
    {
        if (paramIds.Count == 0) return "SELECT 0 AS cnt";
        var filter = BuildOutlierWhereClause(
            tasookNo,
            satelliteNo,
            testBatchId,
            paramIds,
            windowStart,
            windowEnd,
            paramIdFilter);
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
            list.Add(ParseHqParamPointRow(root, pid, ts, v));
        }

        return list;
    }

    private static HqParamPointRow ParseHqParamPointRow(
        JsonElement root,
        string pid,
        DateTimeOffset ts,
        double v)
    {
        var isOutlier = root.TryGetProperty("is_outlier", out var oEl) && oEl.ValueKind == JsonValueKind.Number
            && oEl.TryGetUInt32(out var o)
            && o != 0;
        var isConfirmed = root.TryGetProperty("is_confirmed_outlier", out var cEl) && cEl.ValueKind == JsonValueKind.Number
            && cEl.TryGetUInt32(out var c)
            && c != 0;
        return new HqParamPointRow(pid, ts, v, isOutlier, isConfirmed);
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");

    private static string ToUtcTimestampLiteral(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

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
