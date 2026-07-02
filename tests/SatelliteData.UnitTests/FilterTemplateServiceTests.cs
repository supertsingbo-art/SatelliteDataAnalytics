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

public sealed class FilterTemplateServiceTests
{
    [Fact]
    public async Task CloneAsync_CreatesNewTemplateIdVersionOneDraft()
    {
        var ctx = new TestContext();
        var sourceTemplateId = Guid.NewGuid();
        var source = NewTemplate(sourceTemplateId, 3, "姿态筛选模板", TemplateStatus.Published);
        await ctx.TemplateRepository.SaveAsync(source, CancellationToken.None);

        var detail = await ctx.Service.CloneAsync(sourceTemplateId, source.Version, Guid.NewGuid(), CancellationToken.None);

        Assert.NotEqual(sourceTemplateId, detail.View.TemplateId);
        Assert.Equal(1, detail.View.Version);
        Assert.Equal(TemplateStatus.Draft, detail.View.Status);
        Assert.Contains("(副本)", detail.View.TemplateName);
    }

    [Fact]
    public async Task GetDeleteImpactAsync_ReturnsTaskAndScheduleReferences()
    {
        var ctx = new TestContext();
        var templateId = Guid.NewGuid();
        await ctx.TemplateRepository.SaveAsync(NewTemplate(templateId, 1, "待删模板", TemplateStatus.Draft), CancellationToken.None);

        var run = NewRun(Guid.NewGuid(), templateId, TaskRunStatus.Running);
        await ctx.TaskRuns.InsertAsync(run, CancellationToken.None);
        var schedule = NewSchedule(Guid.NewGuid(), templateId);
        await ctx.Schedules.InsertAsync(schedule, CancellationToken.None);

        var impact = await ctx.Service.GetDeleteImpactAsync(templateId, CancellationToken.None);

        Assert.Equal(1, impact.TaskRunCount);
        Assert.Equal(1, impact.RunningTaskRunCount);
        Assert.Equal(1, impact.ScheduleCount);
        Assert.True(impact.HasReferences);
        Assert.Contains(run.RunId, impact.TaskRunIds);
        Assert.Contains(schedule.ScheduleId, impact.ScheduleIds);
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

        Assert.Equal(TemplateErrorCodes.FilterTemplateInvalidState, ex.ErrorCode);
    }

    [Fact]
    public async Task DeleteTemplateAsync_WithCascade_RemovesTemplateAndRelatedData()
    {
        var ctx = new TestContext();
        var templateId = Guid.NewGuid();
        await ctx.TemplateRepository.SaveAsync(NewTemplate(templateId, 1, "待删模板", TemplateStatus.Published), CancellationToken.None);
        await ctx.TemplateRepository.SaveAsync(NewTemplate(templateId, 2, "待删模板", TemplateStatus.Draft), CancellationToken.None);

        var runningRunId = Guid.NewGuid();
        await ctx.TaskRuns.InsertAsync(NewRun(runningRunId, templateId, TaskRunStatus.Running), CancellationToken.None);
        await ctx.TaskEvents.AppendAsync(
            new TaskEvent(Guid.NewGuid(), runningRunId, "preprocess.created", "Succeeded", null, null, null, DateTimeOffset.UtcNow),
            CancellationToken.None);
        var schedule = NewSchedule(Guid.NewGuid(), templateId);
        await ctx.Schedules.InsertAsync(schedule, CancellationToken.None);

        await ctx.Service.DeleteTemplateAsync(templateId, cascade: true, CancellationToken.None);

        var versions = await ctx.TemplateRepository.GetByTemplateIdAsync(templateId, CancellationToken.None);
        var runs = await ctx.TaskRuns.ListByFilterTemplateIdAsync(templateId, CancellationToken.None);
        var schedules = await ctx.Schedules.ListByFilterTemplateIdAsync(templateId, CancellationToken.None);

        Assert.Empty(versions);
        Assert.Empty(runs);
        Assert.Empty(schedules);
        Assert.Contains(schedule.HangfireRecurringId, ctx.Scheduler.RemovedRecurringIds);
    }

    private static FilterTemplate NewTemplate(Guid templateId, int version, string name, TemplateStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        return new FilterTemplate(
            templateId,
            version,
            name,
            status,
            Guid.NewGuid(),
            ParseJson(
                """
                {
                  "scope": { "groupId": "00000000-0000-0000-0000-000000000001", "referenceTasookNo": "TASK-A", "referenceSatelliteNo": "SAT-001" },
                  "timeWindow": { "mode": "CUSTOM" },
                  "conditionConfig": { "instructions": { "startCommands": [], "endCommands": [] }, "parameters": [], "expression": "" },
                  "targetParams": [{ "paramId": "P1001", "outlier": { "method": "SIGMA", "sigma": 3 } }]
                }
                """),
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
            TaskJobType.Preprocess,
            TaskTriggerType.Api,
            status,
            $"idem-{runId:N}",
            "TASK-A",
            "SAT-001",
            "自定义时间段",
            now.AddHours(-1),
            now,
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
            now);
    }

    private static PreprocessSchedule NewSchedule(Guid scheduleId, Guid templateId)
    {
        var now = DateTimeOffset.UtcNow;
        return new PreprocessSchedule(
            scheduleId,
            "TASK-A",
            "SAT-001",
            templateId,
            1,
            new TimeOnly(8, 0, 0),
            1,
            DateOnly.FromDateTime(DateTime.UtcNow.Date),
            true,
            $"preprocess-schedule-{scheduleId:N}",
            null,
            null,
            null,
            now,
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
            TemplateRepository = new InMemoryFilterTemplateRepository();
            GroupRepository = new InMemorySatelliteGroupRepository();
            GroupMemberRepository = new InMemorySatelliteGroupMemberRepository();
            GroupService = new SatelliteGroupService(GroupRepository, GroupMemberRepository);
            AssetCache = new InMemoryAssetCacheRepository();
            TaskRuns = new InMemoryTaskRunRepository();
            TaskEvents = new InMemoryTaskEventRepository();
            Schedules = new InMemoryPreprocessScheduleRepository();
            Scheduler = new FakeScheduler();
            var validator = new PreprocessTaskValidator(AssetCache, TemplateRepository, GroupService);
            ScheduleService = new PreprocessScheduleService(
                Schedules,
                TaskRuns,
                TaskEvents,
                Scheduler,
                validator,
                NullLogger<PreprocessScheduleService>.Instance);
            Orchestrator = new TaskOrchestrator(
                TaskRuns,
                TaskEvents,
                Scheduler,
                new TaskRunCancellationRegistry(),
                validator,
                ScheduleService,
                NullLogger<TaskOrchestrator>.Instance);
            RunLifecycleService = new TaskRunLifecycleService(
                TaskRuns,
                TaskEvents,
                new InMemoryHqParamMetadataRepository(),
                new InMemoryPreprocessParamClaimRepository(),
                new InMemoryPreprocessOutlierSegmentRepository(),
                new InMemoryPreprocessOutlierPointReviewRepository(),
                Scheduler,
                NullLogger<TaskRunLifecycleService>.Instance);
            Service = new FilterTemplateService(
                TemplateRepository,
                GroupRepository,
                GroupService,
                AssetCache,
                TaskRuns,
                Schedules,
                Orchestrator,
                RunLifecycleService,
                ScheduleService);
        }

        public InMemoryFilterTemplateRepository TemplateRepository { get; }
        public InMemorySatelliteGroupRepository GroupRepository { get; }
        public InMemorySatelliteGroupMemberRepository GroupMemberRepository { get; }
        public SatelliteGroupService GroupService { get; }
        public InMemoryAssetCacheRepository AssetCache { get; }
        public InMemoryTaskRunRepository TaskRuns { get; }
        public InMemoryTaskEventRepository TaskEvents { get; }
        public InMemoryPreprocessScheduleRepository Schedules { get; }
        public FakeScheduler Scheduler { get; }
        public PreprocessScheduleService ScheduleService { get; }
        public TaskOrchestrator Orchestrator { get; }
        public TaskRunLifecycleService RunLifecycleService { get; }
        public FilterTemplateService Service { get; }
    }

    private sealed class FakeScheduler : IBackgroundJobScheduler
    {
        public List<string> RemovedRecurringIds { get; } = [];

        public string EnqueuePreprocess(Guid runId) => $"job-pre-{runId:N}";

        public string SchedulePreprocess(Guid runId, DateTimeOffset runAt) => $"job-schedule-{runId:N}";

        public void AddOrUpdateDailySchedule(Guid scheduleId, string cronExpression)
        {
        }

        public void RemoveDailySchedule(string recurringJobId) => RemovedRecurringIds.Add(recurringJobId);

        public bool TryDeleteScheduledJob(string hangfireJobId) => true;

        public string EnqueueAlgorithm(Guid runId) => $"job-algo-{runId:N}";

        public string EnqueueWebhook(Guid runId) => $"job-webhook-{runId:N}";
    }
}
