using SatelliteData.Application.Tasks;
using SatelliteData.Domain.Tasks;
using Xunit;

namespace SatelliteData.UnitTests;

public sealed class TaskRunStateHelperPipelineTests
{
    [Fact]
    public void CanViewProcessedData_PipelineWithFilter_Succeeded()
    {
        var run = NewPipelineRun(filterTemplateId: Guid.NewGuid(), TaskRunStatus.Succeeded);
        Assert.True(TaskRunStateHelper.CanViewProcessedData(run));
    }

    [Fact]
    public void CanViewProcessedData_PipelineWithoutFilter_Succeeded()
    {
        var run = NewPipelineRun(filterTemplateId: null, TaskRunStatus.Succeeded);
        Assert.False(TaskRunStateHelper.CanViewProcessedData(run));
    }

    [Fact]
    public void CanReExecuteRun_PipelineWithFilter_Terminal()
    {
        var run = NewPipelineRun(filterTemplateId: Guid.NewGuid(), TaskRunStatus.Failed);
        Assert.True(TaskRunStateHelper.CanReExecuteRun(run));
    }

    [Fact]
    public void CanReExecuteRun_PipelineWithoutFilter_Terminal()
    {
        var run = NewPipelineRun(filterTemplateId: null, TaskRunStatus.Failed);
        Assert.False(TaskRunStateHelper.CanReExecuteRun(run));
    }

    private static TaskRun NewPipelineRun(Guid? filterTemplateId, TaskRunStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        return new TaskRun(
            Guid.NewGuid(),
            null,
            "JOB-TEST",
            TaskJobType.Pipeline,
            TaskTriggerType.Api,
            status,
            "key",
            "T1",
            "S1",
            null,
            now.AddDays(-1),
            now,
            filterTemplateId,
            filterTemplateId is null ? null : 1,
            Guid.NewGuid(),
            1,
            null,
            null,
            100m,
            "done",
            now,
            now,
            false,
            null,
            null,
            null,
            now);
    }
}
