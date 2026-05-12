using System.Text.Json;
using SatelliteData.Domain.Assets;

namespace SatelliteData.Application.Pipeline;

public sealed record EffectiveWindow(DateTimeOffset Start, DateTimeOffset End);

public sealed record TargetParamSpec(string ParamId, string OutlierMethod);

public interface IFilterRuleEvaluator
{
    /// <summary>根据筛选模板与测试阶段缓存，得到有效时间窗与目标参数列表。</summary>
    Task<(EffectiveWindow Window, IReadOnlyList<TargetParamSpec> Targets)> EvaluateAsync(
        JsonElement filterConfigJson,
        string tasookNo,
        string satelliteNo,
        string? testBatchId,
        DateTimeOffset? taskWindowStart,
        DateTimeOffset? taskWindowEnd,
        IReadOnlyCollection<TestBatchCache> testBatches,
        CancellationToken cancellationToken);
}
