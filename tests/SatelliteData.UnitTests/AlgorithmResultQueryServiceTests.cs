using SatelliteData.Application.Pipeline;
using SatelliteData.Application.Tasks;
using SatelliteData.Domain.Tasks;
using SatelliteData.Infrastructure.Pipeline;
using Xunit;

namespace SatelliteData.UnitTests;

public sealed class AlgorithmResultQueryServiceTests
{
    [Fact]
    public async Task GetByRunIdAsync_SucceededWithTemplate_ReturnsRows()
    {
        var runId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var ctx = new TestContext();
        await ctx.TaskRuns.InsertAsync(NewRun(runId, templateId, TaskRunStatus.Succeeded), CancellationToken.None);
        ctx.ClickHouse.Rows = [
            new AlgorithmResultRow(
                "node-1",
                "mean",
                "mean_value",
                12.3456,
                "{\"value\":12.3456}",
                DateTimeOffset.UtcNow.AddHours(-1),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow)
        ];

        var result = await ctx.Service.GetByRunIdAsync(runId, CancellationToken.None);

        Assert.Equal(runId, result.RunId);
        Assert.Equal(1, result.Total);
        Assert.Equal("mean", result.Items[0].AlgorithmCode);
        Assert.Equal("mean_value", result.Items[0].MetricName);
    }

    [Fact]
    public async Task GetByRunIdAsync_RunNotFound_ThrowsNotFound()
    {
        var ctx = new TestContext();
        var ex = await Assert.ThrowsAsync<TaskValidationException>(() =>
            ctx.Service.GetByRunIdAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(TaskErrorCodes.NotFound, ex.ErrorCode);
    }

    [Fact]
    public async Task GetByRunIdAsync_FailedRun_ThrowsNoAlgorithmResults()
    {
        var runId = Guid.NewGuid();
        var ctx = new TestContext();
        await ctx.TaskRuns.InsertAsync(NewRun(runId, Guid.NewGuid(), TaskRunStatus.Failed), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<TaskValidationException>(() =>
            ctx.Service.GetByRunIdAsync(runId, CancellationToken.None));

        Assert.Equal(TaskErrorCodes.NoAlgorithmResults, ex.ErrorCode);
    }

    [Fact]
    public async Task GetByRunIdAsync_SucceededWithoutTemplate_ThrowsNoAlgorithmResults()
    {
        var runId = Guid.NewGuid();
        var ctx = new TestContext();
        await ctx.TaskRuns.InsertAsync(NewRun(runId, null, TaskRunStatus.Succeeded), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<TaskValidationException>(() =>
            ctx.Service.GetByRunIdAsync(runId, CancellationToken.None));

        Assert.Equal(TaskErrorCodes.NoAlgorithmResults, ex.ErrorCode);
    }

    private static TaskRun NewRun(Guid runId, Guid? algorithmTemplateId, TaskRunStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        return new TaskRun(
            runId,
            null,
            $"RUN-{runId:N}",
            TaskJobType.Pipeline,
            TaskTriggerType.Api,
            status,
            $"idem-{runId:N}",
            "TASK-A",
            "SAT-001",
            "自定义时间段",
            now.AddHours(-1),
            now,
            null,
            null,
            algorithmTemplateId,
            algorithmTemplateId.HasValue ? 1 : null,
            null,
            null,
            100m,
            "algorithm_done",
            now.AddMinutes(-5),
            now,
            false,
            null,
            null,
            null,
            now);
    }

    private sealed class TestContext
    {
        public InMemoryTaskRunRepository TaskRuns { get; } = new();
        public FakeClickHouseGateway ClickHouse { get; } = new();
        public AlgorithmResultQueryService Service { get; }

        public TestContext()
        {
            Service = new AlgorithmResultQueryService(TaskRuns, ClickHouse);
        }
    }

    private sealed class FakeClickHouseGateway : IClickHouseGateway
    {
        public IReadOnlyList<AlgorithmResultRow> Rows { get; set; } = [];

        public Task ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task EnsureHqParamPointTableAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task EnsureAlgoResultTableAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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
            CancellationToken cancellationToken) => Task.FromResult(0L);

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
            CancellationToken cancellationToken) => Task.FromResult(0L);

        public Task<IReadOnlyList<AggregatedSeriesPoint>> QueryAggregatedSeriesAsync(
            string tasookNo,
            string satelliteNo,
            string testBatchId,
            string paramId,
            DateTimeOffset windowStart,
            DateTimeOffset windowEnd,
            int bucketSeconds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AggregatedSeriesPoint>>(Array.Empty<AggregatedSeriesPoint>());

        public Task<IReadOnlyList<HqParamPointRow>> QueryOutlierPointsForChartAsync(
            string tasookNo,
            string satelliteNo,
            string testBatchId,
            IReadOnlyList<string> paramIds,
            DateTimeOffset windowStart,
            DateTimeOffset windowEnd,
            int maxOutlierPoints,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HqParamPointRow>>(Array.Empty<HqParamPointRow>());

        public Task DeleteByClaimsAsync(
            string tasookNo,
            string satelliteNo,
            string testBatchId,
            IReadOnlyList<PreprocessParamClaimRequest> claims,
            ulong keepVersionFromInclusive,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<AlgorithmResultRow>> QueryAlgorithmResultsAsync(
            Guid runId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Rows);
    }
}
