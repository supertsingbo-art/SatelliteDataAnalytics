using SatelliteData.Application.Assets;
using SatelliteData.Application.Pipeline;
using SatelliteData.Domain.Assets;
using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

public sealed class TaskRunProcessedDataService(
    ITaskRunRepository taskRuns,
    IHqParamMetadataRepository hqMetadata,
    IAssetCacheRepository assetCache,
    IClickHouseGateway clickHouse,
    IPreprocessOutlierSegmentRepository outlierSegments,
    IPreprocessOutlierPointReviewRepository outlierReviews,
    IPreprocessValidRangeRepository validRanges)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
    public const int DefaultSeriesMaxPoints = 3000;
    public const int MinSeriesMaxPoints = 500;
    public const int MaxSeriesMaxPoints = 5000;
    public const int MaxSeriesParams = 8;
    public const int MaxOutlierPointsForChart = 10_000;

    public async Task<TaskProcessedDataDto> GetProcessedDataAsync(
        Guid runId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var ctx = await LoadQueryContextAsync(runId, cancellationToken).ConfigureAwait(false);
        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        if (ctx.ParamIds.Count == 0)
        {
            return new TaskProcessedDataDto(runId, [], [], 0, safePage, safePageSize);
        }

        var total = await clickHouse.CountDistinctTimestampsAsync(
            ctx.Run.TasookNo,
            ctx.Run.SatelliteNo,
            ctx.TestBatchId,
            ctx.ParamIds,
            ctx.WindowStart,
            ctx.WindowEnd,
            cancellationToken).ConfigureAwait(false);

        var points = total == 0
            ? []
            : await clickHouse.QueryHqParamPointsByTimestampPageAsync(
                ctx.Run.TasookNo,
                ctx.Run.SatelliteNo,
                ctx.TestBatchId,
                ctx.ParamIds,
                ctx.WindowStart,
                ctx.WindowEnd,
                safePage,
                safePageSize,
                cancellationToken).ConfigureAwait(false);

        var columns = BuildColumns(ctx.ParamIds, ctx.Parameters);
        var reviews = await outlierReviews.ListByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        var reviewByKey = reviews.ToDictionary(
            r => ReviewCellKey(r.ParamId, r.Ts),
            r => r.ReviewStatus,
            StringComparer.Ordinal);
        var rows = BuildMatrixRows(points, reviewByKey);

        return new TaskProcessedDataDto(runId, columns, rows, total, safePage, safePageSize);
    }

    public async Task<TaskOutlierPointsDto> GetOutlierPointsAsync(
        Guid runId,
        int page,
        int pageSize,
        string? paramId,
        string? status,
        CancellationToken cancellationToken)
    {
        var ctx = await LoadQueryContextAsync(runId, cancellationToken).ConfigureAwait(false);
        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var paramFilter = string.IsNullOrWhiteSpace(paramId) ? null : paramId.Trim();
        var statusFilter = NormalizeReviewStatusFilter(status);

        if (paramFilter is not null
            && !ctx.ParamIds.Contains(paramFilter, StringComparer.Ordinal))
        {
            throw new TaskValidationException(TaskErrorCodes.NoProcessedData, $"参数不在本任务目标列表中：{paramFilter}");
        }

        var (reviews, total) = await outlierReviews
            .ListPageAsync(runId, statusFilter, paramFilter, safePage, safePageSize, cancellationToken)
            .ConfigureAwait(false);

        var items = reviews
            .Select(r =>
            {
                ctx.Parameters.TryGetValue(r.ParamId, out var p);
                return new TaskOutlierPointItemDto(
                    r.ReviewId,
                    r.ParamId,
                    p?.DisplayLabel ?? r.ParamId,
                    r.Ts.ToString("O"),
                    r.AutoValue ?? 0,
                    r.AutoOutlierMethod ?? "SIGMA",
                    r.ReviewStatus,
                    r.Remark);
            })
            .ToList();

        return new TaskOutlierPointsDto(runId, items, total, safePage, safePageSize);
    }

    public async Task<TaskOutlierSegmentsDto> GetOutlierSegmentsAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var ctx = await LoadQueryContextAsync(runId, cancellationToken).ConfigureAwait(false);
        var reviewCompleted = string.Equals(
            ctx.Run.OutlierReviewStatus,
            OutlierReviewRunStatus.Completed,
            StringComparison.Ordinal);
        var segmentKind = reviewCompleted ? OutlierSegmentKind.Confirmed : OutlierSegmentKind.Auto;
        var segments = await outlierSegments
            .ListByRunIdAndKindAsync(runId, segmentKind, cancellationToken)
            .ConfigureAwait(false);

        var items = segments
            .OrderBy(s => s.ParamId, StringComparer.Ordinal)
            .ThenBy(s => s.SegmentStart)
            .Select(s =>
            {
                ctx.Parameters.TryGetValue(s.ParamId, out var p);
                var duration = (s.SegmentEnd - s.SegmentStart).TotalSeconds;
                if (duration < 0)
                {
                    duration = 0;
                }

                return new TaskOutlierSegmentItemDto(
                    s.ParamId,
                    p?.DisplayLabel ?? s.ParamId,
                    s.SegmentStart.ToString("O"),
                    s.SegmentEnd.ToString("O"),
                    string.IsNullOrWhiteSpace(s.OutlierMethod) ? "SIGMA" : s.OutlierMethod,
                    duration,
                    s.SegmentKind);
            })
            .ToList();

        return new TaskOutlierSegmentsDto(runId, items, items.Count, segmentKind, reviewCompleted);
    }

    public async Task<TaskValidRangesDto> GetValidRangesAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        await LoadQueryContextAsync(runId, cancellationToken).ConfigureAwait(false);
        var rows = await validRanges.ListByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        var items = rows
            .OrderBy(x => x.RangeStart)
            .Select(x =>
            {
                var duration = (x.RangeEnd - x.RangeStart).TotalSeconds;
                if (duration < 0)
                {
                    duration = 0;
                }

                return new TaskValidRangeItemDto(
                    x.RangeStart.ToString("O"),
                    x.RangeEnd.ToString("O"),
                    duration);
            })
            .ToArray();

        return new TaskValidRangesDto(runId, items, items.Length);
    }

    public async Task<TaskProcessedSeriesDto> GetProcessedSeriesAsync(
        Guid runId,
        IReadOnlyList<string>? paramIds,
        DateTimeOffset? windowStart,
        DateTimeOffset? windowEnd,
        int maxPoints,
        CancellationToken cancellationToken)
    {
        var ctx = await LoadQueryContextAsync(runId, cancellationToken).ConfigureAwait(false);
        var safeMaxPoints = Math.Clamp(maxPoints, MinSeriesMaxPoints, MaxSeriesMaxPoints);
        var effectiveWindowStart = windowStart ?? ctx.WindowStart;
        var effectiveWindowEnd = windowEnd ?? ctx.WindowEnd;
        if (effectiveWindowEnd < effectiveWindowStart)
        {
            throw new TaskValidationException(TaskErrorCodes.NoProcessedData, "时间窗无效：结束时间早于开始时间");
        }

        var selectedParamIds = ResolveSeriesParamIds(paramIds, ctx.ParamIds);
        var bucketSeconds = ProcessedSeriesBucketCalculator.ComputeBucketSeconds(
            effectiveWindowStart,
            effectiveWindowEnd,
            safeMaxPoints);

        var reviews = await outlierReviews.ListByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        var reviewByKey = reviews.ToDictionary(
            r => ReviewCellKey(r.ParamId, r.Ts),
            r => r.ReviewStatus,
            StringComparer.Ordinal);

        var series = new List<ParamSeriesDto>(selectedParamIds.Count);
        foreach (var paramId in selectedParamIds)
        {
            ctx.Parameters.TryGetValue(paramId, out var param);
            var rawPointCount = await clickHouse.CountParamPointsInWindowAsync(
                ctx.Run.TasookNo,
                ctx.Run.SatelliteNo,
                ctx.TestBatchId,
                paramId,
                effectiveWindowStart,
                effectiveWindowEnd,
                cancellationToken).ConfigureAwait(false);
            var buckets = await clickHouse.QueryAggregatedSeriesAsync(
                ctx.Run.TasookNo,
                ctx.Run.SatelliteNo,
                ctx.TestBatchId,
                paramId,
                effectiveWindowStart,
                effectiveWindowEnd,
                bucketSeconds,
                cancellationToken).ConfigureAwait(false);
            var points = buckets
                .Select(b => new SeriesBucketPointDto(
                    b.Ts.ToString("O"),
                    b.MinValue,
                    b.MaxValue,
                    b.AvgValue,
                    b.PointCount))
                .ToList();
            series.Add(new ParamSeriesDto(
                paramId,
                param?.DisplayLabel ?? paramId,
                points,
                rawPointCount));
        }

        var outliersTotal = await clickHouse.CountOutlierPointsAsync(
            ctx.Run.TasookNo,
            ctx.Run.SatelliteNo,
            ctx.TestBatchId,
            selectedParamIds,
            effectiveWindowStart,
            effectiveWindowEnd,
            null,
            cancellationToken).ConfigureAwait(false);
        var outlierRows = outliersTotal == 0
            ? []
            : await clickHouse.QueryOutlierPointsForChartAsync(
                ctx.Run.TasookNo,
                ctx.Run.SatelliteNo,
                ctx.TestBatchId,
                selectedParamIds,
                effectiveWindowStart,
                effectiveWindowEnd,
                MaxOutlierPointsForChart,
                cancellationToken).ConfigureAwait(false);
        var outliers = outlierRows
            .Select(pt =>
            {
                ctx.Parameters.TryGetValue(pt.ParamId, out var p);
                reviewByKey.TryGetValue(ReviewCellKey(pt.ParamId, pt.Ts), out var reviewStatus);
                return new SeriesOutlierPointDto(
                    pt.ParamId,
                    p?.DisplayLabel ?? pt.ParamId,
                    pt.Ts.ToString("O"),
                    pt.Value,
                    pt.IsOutlier,
                    pt.IsConfirmedOutlier,
                    reviewStatus);
            })
            .ToList();

        return new TaskProcessedSeriesDto(
            runId,
            effectiveWindowStart.ToString("O"),
            effectiveWindowEnd.ToString("O"),
            safeMaxPoints,
            bucketSeconds,
            series,
            outliers,
            outliersTotal > MaxOutlierPointsForChart,
            outliersTotal);
    }

    private static IReadOnlyList<string> ResolveSeriesParamIds(
        IReadOnlyList<string>? paramIds,
        IReadOnlyList<string> allowedParamIds)
    {
        if (allowedParamIds.Count == 0)
        {
            return [];
        }

        if (paramIds is null || paramIds.Count == 0)
        {
            throw new TaskValidationException(TaskErrorCodes.NoProcessedData, "请至少选择一个参数");
        }

        var distinct = paramIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (distinct.Count == 0)
        {
            throw new TaskValidationException(TaskErrorCodes.NoProcessedData, "请至少选择一个参数");
        }

        if (distinct.Count > MaxSeriesParams)
        {
            throw new TaskValidationException(
                TaskErrorCodes.NoProcessedData,
                $"每次最多选择 {MaxSeriesParams} 个参数");
        }

        foreach (var paramId in distinct)
        {
            if (!allowedParamIds.Contains(paramId, StringComparer.Ordinal))
            {
                throw new TaskValidationException(
                    TaskErrorCodes.NoProcessedData,
                    $"参数不在本任务目标列表中：{paramId}");
            }
        }

        return distinct;
    }

    private static string? NormalizeReviewStatusFilter(string? status)
    {
        if (string.IsNullOrWhiteSpace(status) || string.Equals(status, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var normalized = status.Trim().ToUpperInvariant();
        if (normalized.Length == 0)
        {
            return null;
        }

        return normalized;
    }

    private async Task<ProcessedDataQueryContext> LoadQueryContextAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var run = await taskRuns.GetByRunIdAsync(runId, cancellationToken).ConfigureAwait(false)
            ?? throw new TaskValidationException(TaskErrorCodes.NotFound, "任务不存在");

        if (!TaskRunStateHelper.CanViewProcessedData(run))
        {
            throw new TaskValidationException(
                TaskErrorCodes.NoProcessedData,
                "仅执行成功且包含预处理落盘的任务可查看数据明细");
        }

        if (run.WindowStart is null || run.WindowEnd is null)
        {
            throw new TaskValidationException(TaskErrorCodes.NoProcessedData, "任务缺少数据时间窗");
        }

        var metaRows = await hqMetadata.ListByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        var paramIds = metaRows.Select(m => m.ParamId).Distinct(StringComparer.Ordinal).ToList();
        var testBatchId = metaRows.Count > 0 ? metaRows[0].TestBatchId : run.TestBatchName ?? "";
        var windowStart = metaRows.Count > 0 ? metaRows.Min(m => m.WindowStart) : run.WindowStart.Value;
        var windowEnd = metaRows.Count > 0 ? metaRows.Max(m => m.WindowEnd) : run.WindowEnd.Value;
        var outlierMethodByParam = metaRows
            .GroupBy(m => m.ParamId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.First().OutlierMethod ?? "SIGMA",
                StringComparer.Ordinal);

        var parameters = (await assetCache.GetParametersAsync(run.TasookNo, run.SatelliteNo, cancellationToken)
            .ConfigureAwait(false)).ToDictionary(p => p.ParamId, StringComparer.Ordinal);

        return new ProcessedDataQueryContext(
            run,
            testBatchId,
            windowStart,
            windowEnd,
            paramIds,
            parameters,
            outlierMethodByParam);
    }

    private static IReadOnlyList<TaskProcessedDataColumnDto> BuildColumns(
        IReadOnlyList<string> paramIds,
        IReadOnlyDictionary<string, ParamCache> parameters) =>
        paramIds
            .Select(pid =>
            {
                parameters.TryGetValue(pid, out var p);
                return new TaskProcessedDataColumnDto(pid, p?.DisplayLabel ?? pid);
            })
            .ToList();

    private static string ReviewCellKey(string paramId, DateTimeOffset ts) => $"{paramId}|{ts:O}";

    private static IReadOnlyList<TaskProcessedDataRowDto> BuildMatrixRows(
        IReadOnlyList<HqParamPointRow> points,
        IReadOnlyDictionary<string, string> reviewByKey)
    {
        var byTs = new SortedDictionary<DateTimeOffset, Dictionary<string, TaskProcessedDataCellDto>>();
        foreach (var pt in points)
        {
            if (!byTs.TryGetValue(pt.Ts, out var cells))
            {
                cells = new Dictionary<string, TaskProcessedDataCellDto>(StringComparer.Ordinal);
                byTs[pt.Ts] = cells;
            }

            reviewByKey.TryGetValue(ReviewCellKey(pt.ParamId, pt.Ts), out var reviewStatus);
            cells[pt.ParamId] = new TaskProcessedDataCellDto(
                pt.Value,
                pt.IsOutlier,
                pt.IsConfirmedOutlier,
                reviewStatus);
        }

        return byTs
            .Select(kv => new TaskProcessedDataRowDto(kv.Key.ToString("O"), kv.Value))
            .ToList();
    }

    private sealed record ProcessedDataQueryContext(
        TaskRun Run,
        string TestBatchId,
        DateTimeOffset WindowStart,
        DateTimeOffset WindowEnd,
        IReadOnlyList<string> ParamIds,
        IReadOnlyDictionary<string, ParamCache> Parameters,
        IReadOnlyDictionary<string, string> OutlierMethodByParam);
}
