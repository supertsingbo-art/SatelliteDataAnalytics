using SatelliteData.Application.Tasks;
using Xunit;

namespace SatelliteData.UnitTests;

public sealed class TaskRunCancellationRegistryTests
{
    [Fact]
    public void Cancel_TriggersRegisteredToken()
    {
        var registry = new TaskRunCancellationRegistry();
        var runId = Guid.NewGuid();

        using var reg = registry.Register(runId);
        Assert.False(reg.Token.IsCancellationRequested);

        registry.Cancel(runId);

        Assert.True(reg.Token.IsCancellationRequested);
    }

    [Fact]
    public void Cancel_UnknownRunId_DoesNotThrow()
    {
        var registry = new TaskRunCancellationRegistry();
        registry.Cancel(Guid.NewGuid());
    }

    [Fact]
    public void Dispose_UnregistersRun()
    {
        var registry = new TaskRunCancellationRegistry();
        var runId = Guid.NewGuid();

        var reg = registry.Register(runId);
        reg.Dispose();

        registry.Cancel(runId);
        Assert.False(reg.Token.IsCancellationRequested);
    }
}
