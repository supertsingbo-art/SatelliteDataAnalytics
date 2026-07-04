using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SatelliteData.Application.Assets;
using SatelliteData.Application.Tasks;
using SatelliteData.Application.Templates;
using SatelliteData.Domain.Tasks;
using SatelliteData.Domain.Templates;
using SatelliteData.Infrastructure.Pipeline;
using SatelliteData.Infrastructure.PostgreSql;
using Xunit;

namespace SatelliteData.UnitTests;

public sealed class AlgorithmTemplateServiceTests
{
    [Fact]
    public async Task CloneAsync_CreatesNewTemplateIdVersionOneDraft()
    {
        var ctx = new TestContext();
        var sourceTemplateId = Guid.NewGuid();
        var source = NewTemplate(sourceTemplateId, 3, "姿态算法模板", TemplateStatus.Published);
        await ctx.TemplateRepository.SaveAsync(source, CancellationToken.None);

        var detail = await ctx.Service.CloneAsync(sourceTemplateId, source.Version, Guid.NewGuid(), CancellationToken.None);

        Assert.NotEqual(sourceTemplateId, detail.View.TemplateId);
        Assert.Equal(1, detail.View.Version);
        Assert.Equal(TemplateStatus.Draft, detail.View.Status);
        Assert.Contains("(副本)", detail.View.TemplateName);
    }

    [Fact]
    public async Task GetDeleteImpactAsync_ReturnsTaskRunCounts()
    {
        var ctx = new TestContext();
        var templateId = Guid.NewGuid();
        await ctx.TemplateRepository.SaveAsync(NewTemplate(templateId, 1, "待删模板", TemplateStatus.Draft), CancellationToken.None);

        var run = NewRun(Guid.NewGuid(), templateId, TaskRunStatus.Running);
        await ctx.TaskRuns.InsertAsync(run, CancellationToken.None);

        var impact = await ctx.Service.GetDeleteImpactAsync(templateId, CancellationToken.None);

        Assert.Equal(1, impact.TaskRunCount);
        Assert.Equal(1, impact.RunningTaskRunCount);
        Assert.True(impact.HasReferences);
        Assert.Contains(run.RunId, impact.TaskRunIds);
    }

    [Fact]
    public async Task DeleteTemplateAsync_WithoutCascade_ThrowsWhenReferenced()
    {
        var ctx = new TestContext();
        var templateId = Guid.NewGuid();
        await ctx.TemplateRepository.SaveAsync(NewTemplate(templateId, 1, "待删模板", TemplateStatus.Draft), CancellationToken.None);
        await ctx.TaskRuns.InsertAsync(NewRun(Guid.NewGuid(), templateId, TaskRunStatus.Queued), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<TemplateGovernanceException>(() =>
            ctx.Service.DeleteTemplateAsync(templateId, cascade: false, CancellationToken.None));

        Assert.Equal(TemplateErrorCodes.AlgorithmTemplateInvalidState, ex.ErrorCode);
    }

    [Fact]
    public async Task DeleteTemplateAsync_WithCascade_RemovesTemplateAndRelatedRuns()
    {
        var ctx = new TestContext();
        var templateId = Guid.NewGuid();
        await ctx.TemplateRepository.SaveAsync(NewTemplate(templateId, 1, "待删模板", TemplateStatus.Published), CancellationToken.None);
        await ctx.TemplateRepository.SaveAsync(NewTemplate(templateId, 2, "待删模板", TemplateStatus.Draft), CancellationToken.None);

        var runningRunId = Guid.NewGuid();
        await ctx.TaskRuns.InsertAsync(NewRun(runningRunId, templateId, TaskRunStatus.Running), CancellationToken.None);
        await ctx.TaskEvents.AppendAsync(
            new TaskEvent(Guid.NewGuid(), runningRunId, "pipeline.created", "Succeeded", null, null, null, DateTimeOffset.UtcNow),
            CancellationToken.None);

        await ctx.Service.DeleteTemplateAsync(templateId, cascade: true, CancellationToken.None);

        var versions = await ctx.TemplateRepository.GetByTemplateIdAsync(templateId, CancellationToken.None);
        var runs = await ctx.TaskRuns.ListByAlgorithmTemplateIdAsync(templateId, CancellationToken.None);

        Assert.Empty(versions);
        Assert.Empty(runs);
    }

    private static AlgorithmTemplate NewTemplate(Guid templateId, int version, string name, TemplateStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        var reactFlow = ParseJson(
            """
            {
              "nodes": [
                { "id": "src_1", "type": "source", "position": { "x": 0, "y": 0 }, "data": { "nodeRef": "src_1" } },
                { "id": "out_1", "type": "output", "position": { "x": 200, "y": 0 }, "data": { "nodeRef": "out_1" } }
              ],
              "edges": [{ "id": "e1", "source": "src_1", "target": "out_1" }]
            }
            """);
        var config = ParseJson("{}");
        return new AlgorithmTemplate(
            templateId,
            version,
            name,
            status,
            reactFlow,
            config,
            2,
            null,
            null,
            now,
            null,
            now,
            status == TemplateStatus.Published ? now : null);
    }

    private static TaskRun NewRun(Guid runId, Guid templateId, TaskRunStatus status)
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
            templateId,
            1,
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
            now);
    }

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class TestContext
    {
        public TestContext()
        {
            TemplateRepository = new InMemoryAlgorithmTemplateRepository();
            TaskRuns = new InMemoryTaskRunRepository();
            TaskEvents = new InMemoryTaskEventRepository();
            Scheduler = new FakeScheduler();
            var packageRepo = new InMemoryAlgorithmPackageRepository();
            Validator = new AlgorithmTemplateValidator(new AlgorithmRegistryService(packageRepo));
            Orchestrator = new TaskOrchestrator(
                TaskRuns,
                TaskEvents,
                Scheduler,
                new TaskRunCancellationRegistry(),
                new PreprocessTaskValidator(
                    new InMemoryAssetCacheRepository(),
                    new InMemoryFilterTemplateRepository(),
                    new SatelliteGroupService(
                        new InMemorySatelliteGroupRepository(),
                        new InMemorySatelliteGroupMemberRepository())),
                new PreprocessScheduleService(
                    new InMemoryPreprocessScheduleRepository(),
                    TaskRuns,
                    TaskEvents,
                    Scheduler,
                    new PreprocessTaskValidator(
                        new InMemoryAssetCacheRepository(),
                        new InMemoryFilterTemplateRepository(),
                        new SatelliteGroupService(
                            new InMemorySatelliteGroupRepository(),
                            new InMemorySatelliteGroupMemberRepository())),
                    NullLogger<PreprocessScheduleService>.Instance),
                NullLogger<TaskOrchestrator>.Instance);
            RunLifecycleService = new TaskRunLifecycleService(
                TaskRuns,
                TaskEvents,
                new InMemoryHqParamMetadataRepository(),
                new InMemoryPreprocessParamClaimRepository(),
                new InMemoryPreprocessOutlierSegmentRepository(),
                new InMemoryPreprocessOutlierPointReviewRepository(),
                new InMemoryPreprocessValidRangeRepository(),
                new InMemoryTaskRunConflictOptionStore(),
                Scheduler,
                NullLogger<TaskRunLifecycleService>.Instance);
            Service = new AlgorithmTemplateService(
                TemplateRepository,
                Validator,
                TaskRuns,
                Orchestrator,
                RunLifecycleService);
        }

        public InMemoryAlgorithmTemplateRepository TemplateRepository { get; }
        public InMemoryTaskRunRepository TaskRuns { get; }
        public InMemoryTaskEventRepository TaskEvents { get; }
        public FakeScheduler Scheduler { get; }
        public AlgorithmTemplateValidator Validator { get; }
        public TaskOrchestrator Orchestrator { get; }
        public TaskRunLifecycleService RunLifecycleService { get; }
        public AlgorithmTemplateService Service { get; }
    }

    private sealed class FakeScheduler : IBackgroundJobScheduler
    {
        public string EnqueuePreprocess(Guid runId) => $"job-pre-{runId:N}";

        public string SchedulePreprocess(Guid runId, DateTimeOffset runAt) => $"job-schedule-{runId:N}";

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
}
