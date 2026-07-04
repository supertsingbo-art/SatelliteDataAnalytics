using SatelliteData.Application.Pipeline;
using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

public sealed record AlgorithmResultItemDto(
    string NodeId,
    string AlgorithmCode,
    string MetricName,
    double MetricValue,
    string DetailJson,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    DateTimeOffset CreatedAt);

public sealed record AlgorithmResultListDto(
    Guid RunId,
    IReadOnlyList<AlgorithmResultItemDto> Items,
    int Total);

public sealed class AlgorithmResultQueryService(
    ITaskRunRepository taskRuns,
    IClickHouseGateway clickHouse)
{
    public async Task<AlgorithmResultListDto> GetByRunIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await taskRuns.GetByRunIdAsync(runId, cancellationToken).ConfigureAwait(false)
            ?? throw new TaskValidationException(TaskErrorCodes.NotFound, "任务不存在");

        if (!TaskRunStateHelper.CanViewAlgorithmResults(run))
        {
            throw new TaskValidationException(
                TaskErrorCodes.NoAlgorithmResults,
                "仅已成功且含算法模板的任务可查看算法结果");
        }

        await clickHouse.EnsureAlgoResultTableAsync(cancellationToken).ConfigureAwait(false);
        var rows = await clickHouse.QueryAlgorithmResultsAsync(runId, cancellationToken).ConfigureAwait(false);
        var items = rows.Select(r => new AlgorithmResultItemDto(
            r.NodeId,
            r.AlgorithmCode,
            r.MetricName,
            r.MetricValue,
            r.DetailJson,
            r.WindowStart,
            r.WindowEnd,
            r.CreatedAt)).ToArray();

        return new AlgorithmResultListDto(runId, items, items.Length);
    }
}
