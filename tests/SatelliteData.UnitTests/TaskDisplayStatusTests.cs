using SatelliteData.Application.Tasks;
using SatelliteData.Domain.Tasks;
using Xunit;

namespace SatelliteData.UnitTests;

public sealed class TaskDisplayStatusTests
{
    [Fact]
    public void ForRun_QueuedWithHangfireJobId_ReturnsRunning()
    {
        var run = NewPreprocessRun(TaskRunStatus.Queued, hangfireJobId: "job-123");
        var display = TaskDisplayStatus.ForRun(run, DateTimeOffset.UtcNow);
        Assert.Equal(TaskDisplayStatus.Running, display);
    }

    [Fact]
    public void ForRun_ImmediatePending_ReturnsPending()
    {
        var run = NewPreprocessRun(TaskRunStatus.Queued, hangfireJobId: null);
        var display = TaskDisplayStatus.ForRun(run, DateTimeOffset.UtcNow);
        Assert.Equal(TaskDisplayStatus.Pending, display);
    }

    [Fact]
    public void ForRun_Running_ReturnsRunning()
    {
        var run = NewPreprocessRun(TaskRunStatus.Running, hangfireJobId: "job-456");
        var display = TaskDisplayStatus.ForRun(run, DateTimeOffset.UtcNow);
        Assert.Equal(TaskDisplayStatus.Running, display);
    }

    private static TaskRun NewPreprocessRun(TaskRunStatus status, string? hangfireJobId)
    {
        var now = DateTimeOffset.UtcNow;
        return new TaskRun(
            Guid.NewGuid(),
            null,
            "PRE-TEST",
            TaskJobType.Preprocess,
            TaskTriggerType.Api,
            status,
            "key",
            "T1",
            "S1",
            null,
            now.AddDays(-1),
            now,
            Guid.NewGuid(),
            1,
            null,
            null,
            null,
            null,
            3m,
            "queued",
            null,
            null,
            false,
            null,
            null,
            null,
            now,
            PreprocessExecutionMode.Immediate,
            null,
            null,
            hangfireJobId);
    }
}
