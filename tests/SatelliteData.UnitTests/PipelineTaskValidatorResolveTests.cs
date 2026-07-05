using SatelliteData.Application.Tasks;
using Xunit;

namespace SatelliteData.UnitTests;

public sealed class PipelineTaskValidatorResolveTests
{
    [Fact]
    public void ResolveFilterTemplate_Disabled_IgnoresFilterIds()
    {
        var (id, version) = PipelineTaskValidator.ResolveFilterTemplate(false, null, null);
        Assert.Null(id);
        Assert.Null(version);
    }

    [Fact]
    public void ResolveFilterTemplate_Enabled_RequiresFilter()
    {
        var ex = Assert.Throws<TaskValidationException>(() =>
            PipelineTaskValidator.ResolveFilterTemplate(true, null, null));
        Assert.Equal(TaskErrorCodes.FilterTemplateRequired, ex.ErrorCode);
    }

    [Fact]
    public void ResolveFilterTemplate_HalfFilled_Throws()
    {
        var ex = Assert.Throws<TaskValidationException>(() =>
            PipelineTaskValidator.ResolveFilterTemplate(null, Guid.NewGuid(), null));
        Assert.Equal(TaskErrorCodes.ValidationFailed, ex.ErrorCode);
    }

    [Fact]
    public void ResolveFilterTemplate_InferFromFilterIds()
    {
        var filterId = Guid.NewGuid();
        var (id, version) = PipelineTaskValidator.ResolveFilterTemplate(null, filterId, 2);
        Assert.Equal(filterId, id);
        Assert.Equal(2, version);
    }
}
