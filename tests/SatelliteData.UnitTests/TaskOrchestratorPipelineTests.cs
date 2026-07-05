using Microsoft.Extensions.Logging.Abstractions;
using SatelliteData.Application.Assets;
using SatelliteData.Application.Pipeline;
using SatelliteData.Application.Tasks;
using SatelliteData.Application.Templates;
using SatelliteData.Domain.Assets;
using SatelliteData.Domain.Tasks;
using SatelliteData.Domain.Templates;
using SatelliteData.Infrastructure.Pipeline;
using SatelliteData.Infrastructure.PostgreSql;
using System.Text.Json;
using Xunit;

namespace SatelliteData.UnitTests;

public sealed class TaskOrchestratorPipelineTests
{
    [Fact]
    public async Task CreatePipelineAsync_WithoutFilter_EnqueuesAlgorithmOnly()
    {
        var ctx = await CreateContextAsync();
        var algoId = Guid.NewGuid();
        await SeedPublishedAlgorithmAsync(ctx, algoId);

        var result = await ctx.Orchestrator.CreatePipelineAsync(
            new PipelineCreateCommand(
                "TASK-A",
                "SAT-001",
                "batch",
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow,
                FilterTemplateId: null,
                FilterTemplateVersion: null,
                algoId,
                1,
                null,
                TaskTriggerType.Api),
            null,
            CancellationToken.None);

        Assert.True(result.Created);
        var run = await ctx.TaskRuns.GetByRunIdAsync(result.RunId, CancellationToken.None);
        Assert.NotNull(run);
        Assert.Equal("algorithm_queued", run!.CurrentStep);
        Assert.Equal(TaskProgressBands.PreprocessMax, run.ProgressPercent);
        Assert.StartsWith("job-algo-", ctx.Scheduler.LastJobId);
        Assert.Equal("algorithm", ctx.Scheduler.LastQueue);
    }

    [Fact]
    public async Task CreatePipelineAsync_WithFilter_EnqueuesPreprocess()
    {
        var ctx = await CreateContextAsync();
        var algoId = Guid.NewGuid();
        var filterId = Guid.NewGuid();
        await SeedPublishedAlgorithmAsync(ctx, algoId);
        await SeedPublishedFilterAsync(ctx, filterId);

        var result = await ctx.Orchestrator.CreatePipelineAsync(
            new PipelineCreateCommand(
                "TASK-A",
                "SAT-001",
                "batch",
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow,
                filterId,
                1,
                algoId,
                1,
                null,
                TaskTriggerType.Api),
            null,
            CancellationToken.None);

        Assert.True(result.Created);
        var run = await ctx.TaskRuns.GetByRunIdAsync(result.RunId, CancellationToken.None);
        Assert.NotNull(run);
        Assert.Equal("preprocess_queued", run!.CurrentStep);
        Assert.StartsWith("job-pre-", ctx.Scheduler.LastJobId);
        Assert.Equal("preprocess", ctx.Scheduler.LastQueue);
    }

    private static async Task<TestContext> CreateContextAsync()
    {
        var taskRuns = new InMemoryTaskRunRepository();
        var taskEvents = new InMemoryTaskEventRepository();
        var scheduler = new TrackingScheduler();
        var assetCache = new InMemoryAssetCacheRepository();
        await assetCache.UpsertSatelliteAsync(
            new SatelliteCache(
                "TASK-A",
                "TASK-A",
                "SAT-001",
                "SAT-001",
                null,
                null,
                "v1",
                DateTimeOffset.UtcNow,
                1,
                0,
                true,
                JsonDocument.Parse("{}").RootElement),
            CancellationToken.None);

        var groupRepo = new InMemorySatelliteGroupRepository();
        var memberRepo = new InMemorySatelliteGroupMemberRepository();
        var groupService = new SatelliteGroupService(groupRepo, memberRepo);
        var filterRepo = new InMemoryFilterTemplateRepository();
        var algoRepo = new InMemoryAlgorithmTemplateRepository();

        var orchestrator = new TaskOrchestrator(
            taskRuns,
            taskEvents,
            scheduler,
            new TaskRunCancellationRegistry(),
            new PreprocessTaskValidator(assetCache, filterRepo, groupService),
            new PipelineTaskValidator(assetCache, filterRepo, algoRepo, groupService),
            new PreprocessScheduleService(
                new InMemoryPreprocessScheduleRepository(),
                taskRuns,
                taskEvents,
                scheduler,
                new PreprocessTaskValidator(assetCache, filterRepo, groupService),
                NullLogger<PreprocessScheduleService>.Instance),
            NullLogger<TaskOrchestrator>.Instance);

        return new TestContext(taskRuns, scheduler, filterRepo, algoRepo, groupRepo, memberRepo, orchestrator);
    }

    private static async Task SeedPublishedAlgorithmAsync(TestContext ctx, Guid templateId)
    {
        var now = DateTimeOffset.UtcNow;
        await ctx.AlgoRepo.SaveAsync(
            new AlgorithmTemplate(
                templateId,
                1,
                "Algo",
                TemplateStatus.Published,
                JsonDocument.Parse("{}").RootElement,
                JsonDocument.Parse("{}").RootElement,
                1,
                null,
                null,
                now,
                null,
                now,
                now),
            CancellationToken.None);
    }

    private static async Task SeedPublishedFilterAsync(TestContext ctx, Guid templateId)
    {
        var groupId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await ctx.GroupRepo.SaveAsync(
            new SatelliteGroup(
                groupId,
                null,
                "G1",
                $"/root/{groupId}/",
                0,
                null,
                now,
                now),
            CancellationToken.None);
        await ctx.MemberRepo.UpsertAsync(
            new SatelliteGroupMember("TASK-A", "SAT-001", groupId, now),
            CancellationToken.None);

        await ctx.FilterRepo.SaveAsync(
            new FilterTemplate(
                templateId,
                1,
                "Filter",
                TemplateStatus.Published,
                groupId,
                JsonDocument.Parse("{}").RootElement,
                null,
                null,
                now,
                null,
                now,
                now),
            CancellationToken.None);
    }

    private sealed record TestContext(
        InMemoryTaskRunRepository TaskRuns,
        TrackingScheduler Scheduler,
        InMemoryFilterTemplateRepository FilterRepo,
        InMemoryAlgorithmTemplateRepository AlgoRepo,
        InMemorySatelliteGroupRepository GroupRepo,
        InMemorySatelliteGroupMemberRepository MemberRepo,
        TaskOrchestrator Orchestrator);

    private sealed class TrackingScheduler : IBackgroundJobScheduler
    {
        public string? LastJobId { get; private set; }
        public string? LastQueue { get; private set; }

        public string EnqueuePreprocess(Guid runId)
        {
            LastQueue = "preprocess";
            LastJobId = $"job-pre-{runId:N}";
            return LastJobId;
        }

        public string SchedulePreprocess(Guid runId, DateTimeOffset runAt) => EnqueuePreprocess(runId);

        public void AddOrUpdateDailySchedule(Guid scheduleId, string cronExpression)
        {
        }

        public void RemoveDailySchedule(string recurringJobId)
        {
        }

        public bool TryDeleteScheduledJob(string hangfireJobId) => true;

        public string EnqueueAlgorithm(Guid runId)
        {
            LastQueue = "algorithm";
            LastJobId = $"job-algo-{runId:N}";
            return LastJobId;
        }

        public string EnqueueWebhook(Guid runId) => $"job-webhook-{runId:N}";
    }
}
