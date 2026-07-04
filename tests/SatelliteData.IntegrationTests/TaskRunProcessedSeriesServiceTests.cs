using System.Text.Json;
using SatelliteData.Application.Pipeline;
using SatelliteData.Application.Tasks;
using SatelliteData.Domain.Assets;
using SatelliteData.Domain.Tasks;
using SatelliteData.Infrastructure.Pipeline;
using SatelliteData.Infrastructure.PostgreSql;
using Xunit;

namespace SatelliteData.IntegrationTests;

public sealed class TaskRunProcessedSeriesServiceTests
{
    [Fact]
    public async Task GetProcessedSeriesAsync_ThrowsWhenParamNotInRunTargets()
    {
        var fixture = await CreateFixtureAsync(["1001"]);

        var ex = await Assert.ThrowsAsync<TaskValidationException>(() =>
            fixture.Service.GetProcessedSeriesAsync(
                fixture.RunId,
                ["9999"],
                null,
                null,
                TaskRunProcessedDataService.DefaultSeriesMaxPoints,
                CancellationToken.None));

        Assert.Equal(TaskErrorCodes.NoProcessedData, ex.ErrorCode);
        Assert.Contains("9999", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetProcessedSeriesAsync_ThrowsWhenSelectingMoreThanMaxParams()
    {
        var paramIds = Enumerable.Range(1, 9).Select(i => (1000 + i).ToString()).ToArray();
        var fixture = await CreateFixtureAsync(paramIds);

        var ex = await Assert.ThrowsAsync<TaskValidationException>(() =>
            fixture.Service.GetProcessedSeriesAsync(
                fixture.RunId,
                paramIds,
                null,
                null,
                TaskRunProcessedDataService.DefaultSeriesMaxPoints,
                CancellationToken.None));

        Assert.Equal(TaskErrorCodes.NoProcessedData, ex.ErrorCode);
        Assert.Contains("最多选择", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetProcessedSeriesAsync_ReturnsTruncatedOutliersAndReviewStatus()
    {
        var fixture = await CreateFixtureAsync(["1001"]);
        fixture.ClickHouse.ParamPointCounts["1001"] = 1234;
        fixture.ClickHouse.AggregatedSeriesByParam["1001"] =
        [
            new AggregatedSeriesPoint(
                fixture.WindowStart,
                10.0,
                14.0,
                12.0,
                5)
        ];
        fixture.ClickHouse.OutlierTotal = TaskRunProcessedDataService.MaxOutlierPointsForChart + 8;
        fixture.ClickHouse.OutlierRows =
        [
            new HqParamPointRow("1001", fixture.WindowStart.AddSeconds(10), 12.3, true, false)
        ];

        await fixture.OutlierReviews.InsertBatchAsync(
            [
                new PreprocessOutlierPointReview(
                    Guid.NewGuid(),
                    fixture.RunId,
                    "TASK-A",
                    "SAT-001",
                    "1001",
                    fixture.WindowStart.AddSeconds(10),
                    12.3,
                    "SIGMA",
                    OutlierReviewPointStatus.Confirmed,
                    DateTimeOffset.UtcNow,
                    "tester",
                    null,
                    DateTimeOffset.UtcNow)
            ],
            CancellationToken.None);

        var dto = await fixture.Service.GetProcessedSeriesAsync(
            fixture.RunId,
            ["1001"],
            fixture.WindowStart,
            fixture.WindowEnd,
            100,
            CancellationToken.None);

        Assert.Equal(500, dto.MaxPoints);
        Assert.Equal(2, dto.BucketSeconds);
        Assert.Equal(2, fixture.ClickHouse.LastBucketSeconds);
        Assert.True(dto.OutliersTruncated);
        Assert.Equal(TaskRunProcessedDataService.MaxOutlierPointsForChart + 8, dto.OutliersTotal);
        Assert.Single(dto.Outliers);
        Assert.Equal(OutlierReviewPointStatus.Confirmed, dto.Outliers[0].ReviewStatus);
        Assert.Single(dto.Series);
        Assert.Equal(1234, dto.Series[0].RawPointCount);
        Assert.Single(dto.Series[0].Points);
        Assert.Equal(10.0, dto.Series[0].Points[0].MinValue);
        Assert.Equal(14.0, dto.Series[0].Points[0].MaxValue);
        Assert.Equal(12.0, dto.Series[0].Points[0].Value);
        Assert.Equal(5, dto.Series[0].Points[0].PointCount);
    }

    private static async Task<SeriesFixture> CreateFixtureAsync(IReadOnlyList<string> paramIds)
    {
        var now = DateTimeOffset.UtcNow;
        var runId = Guid.NewGuid();
        var windowStart = now.AddMinutes(-10);
        var windowEnd = now;
        var taskRuns = new InMemoryTaskRunRepository();
        var hqMetadata = new InMemoryHqParamMetadataRepository();
        var assetCache = new InMemoryAssetCacheRepository();
        var clickHouse = new RecordingClickHouseGateway();
        var outlierSegments = new InMemoryPreprocessOutlierSegmentRepository();
        var outlierReviews = new InMemoryPreprocessOutlierPointReviewRepository();
        var validRanges = new InMemoryPreprocessValidRangeRepository();

        await taskRuns.InsertAsync(
            new TaskRun(
                runId,
                null,
                "PRE-SERIES-TEST",
                TaskJobType.Preprocess,
                TaskTriggerType.Api,
                TaskRunStatus.Succeeded,
                $"idem-{runId:N}",
                "TASK-A",
                "SAT-001",
                "TB-1",
                windowStart,
                windowEnd,
                Guid.NewGuid(),
                1,
                null,
                null,
                null,
                null,
                100m,
                "done",
                now.AddMinutes(-5),
                now,
                false,
                null,
                null,
                null,
                now),
            CancellationToken.None);

        foreach (var paramId in paramIds)
        {
            await hqMetadata.InsertAsync(
                new HqParamMetadataRow(
                    Guid.NewGuid(),
                    runId,
                    "TASK-A",
                    "SAT-001",
                    "TB-1",
                    paramId,
                    windowStart,
                    windowEnd,
                    Guid.NewGuid(),
                    1,
                    "SIGMA",
                    null),
                CancellationToken.None);
        }

        await assetCache.UpsertSatelliteAsync(
            new SatelliteCache(
                "TASK-A",
                "TASK-A",
                "SAT-001",
                "SAT-001",
                null,
                new MongoConnectionInfo("mongodb://localhost:27017", "db", null),
                "v1",
                now,
                paramIds.Count,
                0,
                true,
                ParseJson("{}")),
            CancellationToken.None);

        await assetCache.UpsertParametersAsync(
            paramIds.Select((paramId, idx) => new ParamCache(
                "TASK-A",
                "SAT-001",
                int.Parse(paramId),
                $"P{paramId}",
                $"参数-{idx + 1}",
                "double",
                null,
                null,
                null,
                null,
                idx + 1,
                "v1",
                now,
                ParseJson("{}"))).ToArray(),
            CancellationToken.None);

        var service = new TaskRunProcessedDataService(
            taskRuns,
            hqMetadata,
            assetCache,
            clickHouse,
            outlierSegments,
            outlierReviews,
            validRanges);

        return new SeriesFixture(service, clickHouse, outlierReviews, runId, windowStart, windowEnd);
    }

    private static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private sealed record SeriesFixture(
        TaskRunProcessedDataService Service,
        RecordingClickHouseGateway ClickHouse,
        InMemoryPreprocessOutlierPointReviewRepository OutlierReviews,
        Guid RunId,
        DateTimeOffset WindowStart,
        DateTimeOffset WindowEnd);

    private sealed class RecordingClickHouseGateway : IClickHouseGateway
    {
        public Dictionary<string, long> ParamPointCounts { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, IReadOnlyList<AggregatedSeriesPoint>> AggregatedSeriesByParam { get; } =
            new(StringComparer.Ordinal);
        public IReadOnlyList<HqParamPointRow> OutlierRows { get; set; } = [];
        public long OutlierTotal { get; set; }
        public int LastBucketSeconds { get; private set; }

        public Task ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task EnsureHqParamPointTableAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InsertJsonEachRowAsync(string tableName, IReadOnlyList<string> jsonRows, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task InsertHqParamPointsAsync(IReadOnlyList<HqParamPointInsertRow> rows, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<HqParamPointInsertRow?> QueryLatestPointAsync(
            string tasookNo,
            string satelliteNo,
            string testBatchId,
            string paramId,
            DateTimeOffset ts,
            CancellationToken cancellationToken) =>
            Task.FromResult<HqParamPointInsertRow?>(null);

        public Task InsertReviewedPointVersionsAsync(IReadOnlyList<HqParamPointInsertRow> rows, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<(DateTimeOffset Ts, double Value)>> QueryProcessedSeriesAsync(
            string tasookNo,
            string satelliteNo,
            string testBatchId,
            string paramId,
            DateTimeOffset windowStart,
            DateTimeOffset windowEnd,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<(DateTimeOffset Ts, double Value)>>(Array.Empty<(DateTimeOffset, double)>());

        public Task<IReadOnlyList<HqParamPointRow>> QueryHqParamPointsAsync(
            string tasookNo,
            string satelliteNo,
            string testBatchId,
            IReadOnlyList<string> paramIds,
            DateTimeOffset windowStart,
            DateTimeOffset windowEnd,
            int maxRows,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HqParamPointRow>>(Array.Empty<HqParamPointRow>());

        public Task<long> CountDistinctTimestampsAsync(
            string tasookNo,
            string satelliteNo,
            string testBatchId,
            IReadOnlyList<string> paramIds,
            DateTimeOffset windowStart,
            DateTimeOffset windowEnd,
            CancellationToken cancellationToken) => Task.FromResult(0L);

        public Task<IReadOnlyList<HqParamPointRow>> QueryHqParamPointsByTimestampPageAsync(
            string tasookNo,
            string satelliteNo,
            string testBatchId,
            IReadOnlyList<string> paramIds,
            DateTimeOffset windowStart,
            DateTimeOffset windowEnd,
            int page,
            int pageSize,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HqParamPointRow>>(Array.Empty<HqParamPointRow>());

        public Task<long> CountOutlierPointsAsync(
            string tasookNo,
            string satelliteNo,
            string testBatchId,
            IReadOnlyList<string> paramIds,
            DateTimeOffset windowStart,
            DateTimeOffset windowEnd,
            string? paramIdFilter,
            CancellationToken cancellationToken) => Task.FromResult(OutlierTotal);

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
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HqParamPointRow>>(Array.Empty<HqParamPointRow>());

        public Task<long> CountParamPointsInWindowAsync(
            string tasookNo,
            string satelliteNo,
            string testBatchId,
            string paramId,
            DateTimeOffset windowStart,
            DateTimeOffset windowEnd,
            CancellationToken cancellationToken) =>
            Task.FromResult(ParamPointCounts.TryGetValue(paramId, out var count) ? count : 0L);

        public Task<IReadOnlyList<AggregatedSeriesPoint>> QueryAggregatedSeriesAsync(
            string tasookNo,
            string satelliteNo,
            string testBatchId,
            string paramId,
            DateTimeOffset windowStart,
            DateTimeOffset windowEnd,
            int bucketSeconds,
            CancellationToken cancellationToken)
        {
            LastBucketSeconds = bucketSeconds;
            return Task.FromResult(
                AggregatedSeriesByParam.TryGetValue(paramId, out var rows)
                    ? rows
                    : (IReadOnlyList<AggregatedSeriesPoint>)Array.Empty<AggregatedSeriesPoint>());
        }

        public Task<IReadOnlyList<HqParamPointRow>> QueryOutlierPointsForChartAsync(
            string tasookNo,
            string satelliteNo,
            string testBatchId,
            IReadOnlyList<string> paramIds,
            DateTimeOffset windowStart,
            DateTimeOffset windowEnd,
            int maxOutlierPoints,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                (IReadOnlyList<HqParamPointRow>)OutlierRows
                    .Where(x => paramIds.Contains(x.ParamId, StringComparer.Ordinal))
                    .Take(maxOutlierPoints)
                    .ToArray());

        public Task DeleteByClaimsAsync(
            string tasookNo,
            string satelliteNo,
            string testBatchId,
            IReadOnlyList<PreprocessParamClaimRequest> claims,
            ulong keepVersionFromInclusive,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
