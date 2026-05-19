using SatelliteData.Application.Assets;
using SatelliteData.Application.Pipeline;
using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

public sealed class TaskRunProcessedDataService(
    ITaskRunRepository taskRuns,
    IHqParamMetadataRepository hqMetadata,
    IAssetCacheRepository assetCache,
    IClickHouseGateway clickHouse)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    public async Task<TaskProcessedDataDto> GetProcessedDataAsync(
        Guid runId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var run = await taskRuns.GetByRunIdAsync(runId, cancellationToken).ConfigureAwait(false)
            ?? throw new TaskValidationException(TaskErrorCodes.NotFound, "任务不存在");

        if (!TaskRunStateHelper.CanViewProcessedData(run))
        {
            throw new TaskValidationException(
                TaskErrorCodes.NoProcessedData,
                "仅执行成功的预处理任务可查看数据明细");
        }

        if (run.WindowStart is null || run.WindowEnd is null)
        {
            throw new TaskValidationException(TaskErrorCodes.NoProcessedData, "任务缺少数据时间窗");
        }

        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var metaRows = await hqMetadata.ListByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        var paramIds = metaRows.Select(m => m.ParamId).Distinct(StringComparer.Ordinal).ToList();
        if (paramIds.Count == 0)
        {
            return new TaskProcessedDataDto(runId, [], [], 0, safePage, safePageSize);
        }

        var testBatchId = metaRows[0].TestBatchId;
        var windowStart = metaRows.Min(m => m.WindowStart);
        var windowEnd = metaRows.Max(m => m.WindowEnd);

        var parameters = (await assetCache.GetParametersAsync(run.TasookNo, run.SatelliteNo, cancellationToken)
            .ConfigureAwait(false)).ToDictionary(p => p.ParamId, StringComparer.Ordinal);

        var total = await clickHouse.CountDistinctTimestampsAsync(
            run.TasookNo,
            run.SatelliteNo,
            testBatchId,
            paramIds,
            windowStart,
            windowEnd,
            cancellationToken).ConfigureAwait(false);

        var points = total == 0
            ? []
            : await clickHouse.QueryHqParamPointsByTimestampPageAsync(
                run.TasookNo,
                run.SatelliteNo,
                testBatchId,
                paramIds,
                windowStart,
                windowEnd,
                safePage,
                safePageSize,
                cancellationToken).ConfigureAwait(false);

        var columns = paramIds
            .Select(pid =>
            {
                parameters.TryGetValue(pid, out var p);
                var label = p?.DisplayLabel ?? pid;
                return new TaskProcessedDataColumnDto(pid, label);
            })
            .ToList();

        var byTs = new SortedDictionary<DateTimeOffset, Dictionary<string, TaskProcessedDataCellDto>>();
        foreach (var pt in points)
        {
            if (!byTs.TryGetValue(pt.Ts, out var cells))
            {
                cells = new Dictionary<string, TaskProcessedDataCellDto>(StringComparer.Ordinal);
                byTs[pt.Ts] = cells;
            }

            cells[pt.ParamId] = new TaskProcessedDataCellDto(pt.Value, pt.IsOutlier);
        }

        var rows = byTs
            .Select(kv => new TaskProcessedDataRowDto(
                kv.Key.ToString("O"),
                kv.Value))
            .ToList();

        return new TaskProcessedDataDto(runId, columns, rows, total, safePage, safePageSize);
    }
}
