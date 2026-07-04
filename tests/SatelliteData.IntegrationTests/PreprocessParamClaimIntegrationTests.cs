using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SatelliteData.Application.Assets;
using SatelliteData.Application.Pipeline;
using SatelliteData.Application.Tasks;
using SatelliteData.Application.Templates;
using SatelliteData.Domain.Assets;
using SatelliteData.Domain.Tasks;
using SatelliteData.Domain.Templates;
using SatelliteData.Infrastructure.Pipeline;
using SatelliteData.Infrastructure.PostgreSql;
using Xunit;

namespace SatelliteData.IntegrationTests;

public sealed class PreprocessParamClaimIntegrationTests
{
    [Fact]
    public async Task ClaimRepository_RejectsOverlapAcrossTemplates()
    {
        var repository = new InMemoryPreprocessParamClaimRepository();
        var now = DateTimeOffset.UtcNow;
        var firstTemplateId = Guid.NewGuid();
        var firstRunId = Guid.NewGuid();
        var secondRunId = Guid.NewGuid();

        var first = await repository.TryAcquireAsync(
            firstRunId,
            "TASK-A",
            "SAT-001",
            firstTemplateId,
            1,
            [new PreprocessParamClaimRequest("P1001", now, now.AddMinutes(5))],
            CancellationToken.None);
        Assert.True(first.Acquired);
        await repository.MarkCommittedByRunIdAsync(firstRunId, CancellationToken.None);

        var second = await repository.TryAcquireAsync(
            secondRunId,
            "TASK-A",
            "SAT-001",
            Guid.NewGuid(),
            2,
            [new PreprocessParamClaimRequest("P1001", now.AddMinutes(2), now.AddMinutes(8))],
            CancellationToken.None);

        Assert.False(second.Acquired);
        Assert.Contains("P1001", second.ConflictParamIds);
        Assert.NotNull(second.ConflictDetail);
        Assert.Equal(firstRunId, second.ConflictDetail!.ConflictRunId);
        Assert.Equal(firstTemplateId, second.ConflictDetail.ConflictFilterTemplateId);
    }

    [Fact]
    public async Task ClaimRepository_AllowsSameRunAfterDeleteByRunId()
    {
        var repository = new InMemoryPreprocessParamClaimRepository();
        var runId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var first = await repository.TryAcquireAsync(
            runId,
            "TASK-A",
            "SAT-001",
            Guid.NewGuid(),
            1,
            [new PreprocessParamClaimRequest("P1001", now, now.AddMinutes(10))],
            CancellationToken.None);
        Assert.True(first.Acquired);
        await repository.MarkCommittedByRunIdAsync(runId, CancellationToken.None);

        await repository.DeleteByRunIdAsync(runId, CancellationToken.None);

        var second = await repository.TryAcquireAsync(
            runId,
            "TASK-A",
            "SAT-001",
            Guid.NewGuid(),
            2,
            [new PreprocessParamClaimRequest("P1001", now.AddMinutes(1), now.AddMinutes(9))],
            CancellationToken.None);
        Assert.True(second.Acquired);
    }

    [Fact]
    public async Task ClaimRepository_ConcurrentAcquire_OnlyOneSucceeds()
    {
        var repository = new InMemoryPreprocessParamClaimRepository();
        var now = DateTimeOffset.UtcNow;
        var claims = new[] { new PreprocessParamClaimRequest("P1001", now, now.AddMinutes(3)) };

        var task1 = repository.TryAcquireAsync(
            Guid.NewGuid(),
            "TASK-A",
            "SAT-001",
            Guid.NewGuid(),
            1,
            claims,
            CancellationToken.None);
        var task2 = repository.TryAcquireAsync(
            Guid.NewGuid(),
            "TASK-A",
            "SAT-001",
            Guid.NewGuid(),
            1,
            claims,
            CancellationToken.None);

        var results = await Task.WhenAll(task1, task2);
        Assert.Equal(1, results.Count(r => r.Acquired));
        Assert.Equal(1, results.Count(r => !r.Acquired));
    }

    [Fact]
    public async Task PreprocessPipeline_WhenClaimConflicts_FailsWithPre006AndParamId()
    {
        var taskRuns = new InMemoryTaskRunRepository();
        var taskEvents = new InMemoryTaskEventRepository();
        var filterTemplates = new InMemoryFilterTemplateRepository();
        var assetCache = new InMemoryAssetCacheRepository();
        var paramClaims = new InMemoryPreprocessParamClaimRepository();
        var outlierSegments = new InMemoryPreprocessOutlierSegmentRepository();
        var outlierReviews = new InMemoryPreprocessOutlierPointReviewRepository();
        var hqMetadata = new InMemoryHqParamMetadataRepository();
        var scheduler = new FakeScheduler();

        var groupRepo = new InMemorySatelliteGroupRepository();
        var memberRepo = new InMemorySatelliteGroupMemberRepository();
        var groupService = new SatelliteGroupService(groupRepo, memberRepo);
        var validator = new PreprocessTaskValidator(assetCache, filterTemplates, groupService);
        var scheduleService = new PreprocessScheduleService(
            new InMemoryPreprocessScheduleRepository(),
            taskRuns,
            taskEvents,
            scheduler,
            validator,
            NullLogger<PreprocessScheduleService>.Instance);

        var pipeline = new PreprocessPipeline(
            taskRuns,
            taskEvents,
            filterTemplates,
            assetCache,
            new MongoConnectionPool(assetCache),
            new FilterRuleEvaluator(NullLogger<FilterRuleEvaluator>.Instance),
            new FakeMongoPkgSeriesReader(),
            new FakeMongoRawSeriesReader(),
            new FakeConditionHistoryProvider(),
            new ConditionRangeEvaluator(NullLogger<ConditionRangeEvaluator>.Instance),
            new DefaultOutlierDetector(),
            new FakeClickHouseGateway(),
            hqMetadata,
            paramClaims,
            outlierSegments,
            outlierReviews,
            new InMemoryPreprocessValidRangeRepository(),
            new InMemoryTaskRunConflictOptionStore(),
            scheduleService,
            scheduler,
            Options.Create(new PipelineOptions { ClickHouseBatchSize = 100 }),
            NullLogger<PreprocessPipeline>.Instance);

        var templateId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var config = ParseJson(
            """
            {
              "scope": { "referenceTasookNo": "TASK-A", "referenceSatelliteNo": "SAT-001" },
              "timeWindow": { "mode": "CUSTOM" },
              "conditionConfig": {
                "instructions": { "startCommands": [], "endCommands": [] },
                "parameters": [],
                "expression": ""
              },
              "durationSeconds": 0,
              "targetParams": [
                {
                  "paramId": "P1001",
                  "outlier": { "method": "SIGMA", "sigma": 3 }
                }
              ]
            }
            """);

        await filterTemplates.SaveAsync(
            new FilterTemplate(
                templateId,
                1,
                "冲突模板测试",
                TemplateStatus.Published,
                Guid.NewGuid(),
                config,
                null,
                null,
                now,
                null,
                now,
                now),
            CancellationToken.None);

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
                1,
                0,
                true,
                ParseJson("{}")),
            CancellationToken.None);
        await assetCache.UpsertParametersAsync(
            [
                new ParamCache(
                    "TASK-A",
                    "SAT-001",
                    1001,
                    "P1001",
                    "P1001",
                    "double",
                    null,
                    null,
                    null,
                    null,
                    1,
                    "v1",
                    now,
                    ParseJson("{}"))
            ],
            CancellationToken.None);

        await taskRuns.InsertAsync(
            new TaskRun(
                runId,
                null,
                "PRE-TEST-001",
                TaskJobType.Preprocess,
                TaskTriggerType.Api,
                TaskRunStatus.Queued,
                "idem-key-001",
                "TASK-A",
                "SAT-001",
                "自定义时间段",
                now,
                now.AddMinutes(10),
                templateId,
                1,
                null,
                null,
                null,
                null,
                0m,
                "queued",
                null,
                null,
                false,
                null,
                null,
                null,
                now),
            CancellationToken.None);

        var conflictTemplateId = Guid.NewGuid();
        var conflictRunId = Guid.NewGuid();
        var occupy = await paramClaims.TryAcquireAsync(
            conflictRunId,
            "TASK-A",
            "SAT-001",
            conflictTemplateId,
            2,
            [new PreprocessParamClaimRequest("P1001", now, now.AddMinutes(10))],
            CancellationToken.None);
        Assert.True(occupy.Acquired);
        await paramClaims.MarkCommittedByRunIdAsync(conflictRunId, CancellationToken.None);

        await pipeline.ExecuteAsync(runId, CancellationToken.None);

        var failed = await taskRuns.GetByRunIdAsync(runId, CancellationToken.None);
        Assert.NotNull(failed);
        Assert.Equal(TaskRunStatus.Failed, failed!.Status);
        Assert.Equal("PRE_006", failed.ErrorCode);
        Assert.Contains("P1001", failed.ErrorMsg);
    }

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class FakeScheduler : IBackgroundJobScheduler
    {
        public string EnqueuePreprocess(Guid runId) => $"job-pre-{runId:N}";

        public string SchedulePreprocess(Guid runId, DateTimeOffset runAt) => $"job-sched-{runId:N}";

        public void AddOrUpdateDailySchedule(Guid scheduleId, string cronExpression)
        {
        }

        public void RemoveDailySchedule(string recurringJobId)
        {
        }

        public bool TryDeleteScheduledJob(string hangfireJobId) => true;

        public string EnqueueAlgorithm(Guid runId) => $"job-algo-{runId:N}";

        public string EnqueueWebhook(Guid runId) => $"job-webhook-{runId:N}";
    }

    private sealed class FakeMongoPkgSeriesReader : IMongoPkgSeriesReader
    {
        public Task<IReadOnlyList<RawSeriesPoint>> ReadSeriesAsync(
            string mongoUri,
            string databaseName,
            int prmSysId,
            int paraId,
            DateTimeOffset windowStart,
            DateTimeOffset windowEnd,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RawSeriesPoint>>(
                [new RawSeriesPoint(windowStart.AddSeconds(1), 1.0)]);
    }

    private sealed class FakeMongoRawSeriesReader : IMongoRawSeriesReader
    {
        public Task<IReadOnlyList<RawSeriesPoint>> ReadSeriesAsync(
            string mongoUri,
            string databaseName,
            string collectionName,
            string tasookNo,
            string satelliteNo,
            string testBatchId,
            string paramId,
            DateTimeOffset windowStart,
            DateTimeOffset windowEnd,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RawSeriesPoint>>(
                [new RawSeriesPoint(windowStart.AddSeconds(1), 1.0)]);
    }

    private sealed class FakeConditionHistoryProvider : IConditionHistoryProvider
    {
        public Task<IReadOnlyDictionary<string, IReadOnlyList<RawSeriesPoint>>> QueryParameterSeriesAsync(
            string mongoUri,
            string mongoDb,
            DateTimeOffset windowStart,
            DateTimeOffset windowEnd,
            IReadOnlyCollection<ParameterHistoryLookup> lookups,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<RawSeriesPoint>>>(
                new Dictionary<string, IReadOnlyList<RawSeriesPoint>>(StringComparer.Ordinal));

        public Task<IReadOnlyList<InstructionHistoryPoint>> QueryInstructionHistoryAsync(
            string mongoUri,
            string mongoDb,
            DateTimeOffset windowStart,
            DateTimeOffset windowEnd,
            IReadOnlyCollection<InstructionHistoryLookup> lookups,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InstructionHistoryPoint>>(Array.Empty<InstructionHistoryPoint>());
    }

    private sealed class FakeClickHouseGateway : IClickHouseGateway
    {
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
