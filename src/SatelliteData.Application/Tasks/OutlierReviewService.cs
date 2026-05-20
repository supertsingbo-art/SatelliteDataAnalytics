using SatelliteData.Application.Assets;
using SatelliteData.Application.Pipeline;
using SatelliteData.Domain.Assets;
using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

public sealed class OutlierReviewService(
    ITaskRunRepository taskRuns,
    IHqParamMetadataRepository hqMetadata,
    IAssetCacheRepository assetCache,
    IPreprocessOutlierPointReviewRepository reviews,
    IPreprocessOutlierSegmentRepository outlierSegments,
    IClickHouseGateway clickHouse)
{
    public async Task<OutlierReviewSummaryDto> GetSummaryAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await LoadRunAsync(runId, cancellationToken).ConfigureAwait(false);
        return new OutlierReviewSummaryDto(
            runId,
            run.OutlierReviewStatus,
            run.OutlierAutoCount,
            run.OutlierPendingCount,
            run.OutlierConfirmedCount,
            run.OutlierJitterCount);
    }

    public async Task<OutlierReviewListDto> ListReviewsAsync(
        Guid runId,
        string? status,
        string? paramId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await LoadRunAsync(runId, cancellationToken).ConfigureAwait(false);
        var ctx = await BuildContextAsync(runId, cancellationToken).ConfigureAwait(false);
        var statusFilter = NormalizeStatusFilter(status);
        var paramFilter = string.IsNullOrWhiteSpace(paramId) ? null : paramId.Trim();

        if (paramFilter is not null
            && !ctx.ParamIds.Contains(paramFilter, StringComparer.Ordinal))
        {
            throw new TaskValidationException(TaskErrorCodes.NoProcessedData, $"参数不在本任务目标列表中：{paramFilter}");
        }

        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, TaskRunProcessedDataService.MaxPageSize);
        var (items, total) = await reviews
            .ListPageAsync(runId, statusFilter, paramFilter, safePage, safePageSize, cancellationToken)
            .ConfigureAwait(false);

        var dtos = items
            .Select(r =>
            {
                ctx.Parameters.TryGetValue(r.ParamId, out var p);
                return new OutlierReviewItemDto(
                    r.ReviewId,
                    r.ParamId,
                    p?.DisplayLabel ?? r.ParamId,
                    r.Ts.ToString("O"),
                    r.AutoValue,
                    r.AutoOutlierMethod ?? "SIGMA",
                    r.ReviewStatus,
                    r.Remark);
            })
            .ToList();

        return new OutlierReviewListDto(runId, dtos, total, safePage, safePageSize);
    }

    public async Task<OutlierReviewSummaryDto> SubmitReviewsAsync(
        Guid runId,
        IReadOnlyList<SubmitOutlierReviewItemDto> items,
        string? reviewedBy,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            throw new TaskValidationException(TaskErrorCodes.ValidationFailed, "复核项不能为空");
        }

        var run = await LoadRunAsync(runId, cancellationToken).ConfigureAwait(false);
        var ctx = await BuildContextAsync(runId, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var updates = new List<OutlierReviewUpdate>();

        foreach (var item in items)
        {
            var status = NormalizePointStatus(item.Status);
            if (status is null)
            {
                throw new TaskValidationException(
                    TaskErrorCodes.ValidationFailed,
                    $"无效的复核状态：{item.Status}，允许 CONFIRMED 或 JITTER");
            }

            if (!DateTimeOffset.TryParse(item.Ts, out var ts))
            {
                throw new TaskValidationException(TaskErrorCodes.ValidationFailed, $"无效时间戳：{item.Ts}");
            }

            updates.Add(new OutlierReviewUpdate(item.ParamId.Trim(), ts, status, item.Remark));
        }

        await reviews.UpdateStatusBatchAsync(runId, updates, now, reviewedBy, cancellationToken)
            .ConfigureAwait(false);

        var chRows = new List<HqParamPointInsertRow>();
        foreach (var u in updates)
        {
            var latest = await clickHouse.QueryLatestPointAsync(
                run.TasookNo,
                run.SatelliteNo,
                ctx.TestBatchId,
                u.ParamId,
                u.Ts,
                cancellationToken).ConfigureAwait(false);
            if (latest is null) continue;

            var isConfirmed = string.Equals(u.Status, OutlierReviewPointStatus.Confirmed, StringComparison.Ordinal)
                ? (byte)1
                : (byte)0;
            chRows.Add(latest with
            {
                IsConfirmedOutlier = isConfirmed,
                Version = latest.Version + 1
            });
        }

        if (chRows.Count > 0)
        {
            await clickHouse.InsertReviewedPointVersionsAsync(chRows, cancellationToken).ConfigureAwait(false);
        }

        run = await SyncRunCountsAsync(runId, cancellationToken).ConfigureAwait(false);
        return new OutlierReviewSummaryDto(
            runId,
            run.OutlierReviewStatus,
            run.OutlierAutoCount,
            run.OutlierPendingCount,
            run.OutlierConfirmedCount,
            run.OutlierJitterCount);
    }

    public async Task<CompleteOutlierReviewResultDto> CompleteReviewAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var run = await LoadRunAsync(runId, cancellationToken).ConfigureAwait(false);
        if (run.OutlierPendingCount > 0)
        {
            throw new TaskValidationException(
                TaskErrorCodes.ValidationFailed,
                $"仍有 {run.OutlierPendingCount} 个离群点待复核，无法完成");
        }

        if (run.OutlierAutoCount == 0)
        {
            throw new TaskValidationException(TaskErrorCodes.ValidationFailed, "本任务无离群点，无需完成复核");
        }

        var ctx = await BuildContextAsync(runId, cancellationToken).ConfigureAwait(false);
        var confirmed = await reviews
            .ListByRunIdAndStatusAsync(runId, OutlierReviewPointStatus.Confirmed, cancellationToken)
            .ConfigureAwait(false);

        await outlierSegments.DeleteByRunIdAndKindAsync(runId, OutlierSegmentKind.Confirmed, cancellationToken)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var newSegments = new List<PreprocessOutlierSegment>();
        foreach (var group in confirmed.GroupBy(c => c.ParamId, StringComparer.Ordinal))
        {
            var paramId = group.Key;
            var confirmedTs = group.Select(c => c.Ts).ToHashSet();
            var series = await clickHouse.QueryProcessedSeriesAsync(
                run.TasookNo,
                run.SatelliteNo,
                ctx.TestBatchId,
                paramId,
                ctx.WindowStart,
                ctx.WindowEnd,
                cancellationToken).ConfigureAwait(false);

            if (series.Count == 0) continue;

            var points = series.Select(s => new RawSeriesPoint(s.Ts, s.Value)).ToList();
            var flags = points.Select(p => confirmedTs.Contains(p.Ts) ? (byte)1 : (byte)0).ToList();
            ctx.OutlierMethodByParam.TryGetValue(paramId, out var method);
            newSegments.AddRange(
                RuleTreeSegmentEvaluator.MergeOutlierSegments(
                    runId,
                    run.TasookNo,
                    run.SatelliteNo,
                    paramId,
                    points,
                    flags,
                    method ?? "SIGMA",
                    now,
                    OutlierSegmentKind.Confirmed));
        }

        if (newSegments.Count > 0)
        {
            await outlierSegments.InsertBatchAsync(newSegments, cancellationToken).ConfigureAwait(false);
        }

        run = run with { OutlierReviewStatus = OutlierReviewRunStatus.Completed };
        await taskRuns.UpdateAsync(run, cancellationToken).ConfigureAwait(false);

        return new CompleteOutlierReviewResultDto(
            runId,
            OutlierReviewRunStatus.Completed,
            newSegments.Count);
    }

    private async Task<TaskRun> SyncRunCountsAsync(Guid runId, CancellationToken cancellationToken) =>
        await SyncRunCountsInternalAsync(runId, cancellationToken).ConfigureAwait(false);

    private async Task<TaskRun> SyncRunCountsInternalAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await taskRuns.GetByRunIdAsync(runId, cancellationToken).ConfigureAwait(false)
            ?? throw new TaskValidationException(TaskErrorCodes.NotFound, "任务不存在");
        var counts = await reviews.CountByStatusAsync(runId, cancellationToken).ConfigureAwait(false);
        counts.TryGetValue(OutlierReviewPointStatus.Pending, out var pending);
        counts.TryGetValue(OutlierReviewPointStatus.Confirmed, out var confirmed);
        counts.TryGetValue(OutlierReviewPointStatus.Jitter, out var jitter);
        var auto = pending + confirmed + jitter;
        var status = run.OutlierReviewStatus;
        if (string.Equals(status, OutlierReviewRunStatus.Completed, StringComparison.Ordinal))
        {
            // keep completed
        }
        else if (auto == 0)
        {
            status = OutlierReviewRunStatus.NotRequired;
        }
        else
        {
            status = OutlierReviewRunStatus.Pending;
        }

        run = run with
        {
            OutlierAutoCount = auto,
            OutlierPendingCount = pending,
            OutlierConfirmedCount = confirmed,
            OutlierJitterCount = jitter,
            OutlierReviewStatus = status
        };
        await taskRuns.UpdateAsync(run, cancellationToken).ConfigureAwait(false);
        return run;
    }

    private async Task<TaskRun> LoadRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await taskRuns.GetByRunIdAsync(runId, cancellationToken).ConfigureAwait(false)
            ?? throw new TaskValidationException(TaskErrorCodes.NotFound, "任务不存在");
        if (!TaskRunStateHelper.CanViewProcessedData(run))
        {
            throw new TaskValidationException(
                TaskErrorCodes.NoProcessedData,
                "仅执行成功的预处理任务可复核离群点");
        }

        return run;
    }

    private async Task<ReviewQueryContext> BuildContextAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await taskRuns.GetByRunIdAsync(runId, cancellationToken).ConfigureAwait(false)
            ?? throw new TaskValidationException(TaskErrorCodes.NotFound, "任务不存在");
        var metaRows = await hqMetadata.ListByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        var paramIds = metaRows.Select(m => m.ParamId).Distinct(StringComparer.Ordinal).ToList();
        var testBatchId = metaRows.Count > 0 ? metaRows[0].TestBatchId : run.TestBatchName ?? "";
        var windowStart = metaRows.Count > 0 ? metaRows.Min(m => m.WindowStart) : run.WindowStart!.Value;
        var windowEnd = metaRows.Count > 0 ? metaRows.Max(m => m.WindowEnd) : run.WindowEnd!.Value;
        var outlierMethodByParam = metaRows
            .GroupBy(m => m.ParamId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().OutlierMethod ?? "SIGMA", StringComparer.Ordinal);
        var parameters = (await assetCache.GetParametersAsync(run.TasookNo, run.SatelliteNo, cancellationToken)
            .ConfigureAwait(false)).ToDictionary(p => p.ParamId, StringComparer.Ordinal);

        return new ReviewQueryContext(run, testBatchId, windowStart, windowEnd, paramIds, parameters, outlierMethodByParam);
    }

    private static string? NormalizeStatusFilter(string? status)
    {
        if (string.IsNullOrWhiteSpace(status) || string.Equals(status, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return status.Trim().ToUpperInvariant() switch
        {
            "PENDING" => OutlierReviewPointStatus.Pending,
            "CONFIRMED" => OutlierReviewPointStatus.Confirmed,
            "JITTER" => OutlierReviewPointStatus.Jitter,
            _ => throw new TaskValidationException(TaskErrorCodes.ValidationFailed, $"无效状态筛选：{status}")
        };
    }

    private static string? NormalizePointStatus(string status) =>
        status.Trim().ToUpperInvariant() switch
        {
            "CONFIRMED" => OutlierReviewPointStatus.Confirmed,
            "JITTER" => OutlierReviewPointStatus.Jitter,
            _ => null
        };

    private sealed record ReviewQueryContext(
        TaskRun Run,
        string TestBatchId,
        DateTimeOffset WindowStart,
        DateTimeOffset WindowEnd,
        IReadOnlyList<string> ParamIds,
        IReadOnlyDictionary<string, ParamCache> Parameters,
        IReadOnlyDictionary<string, string> OutlierMethodByParam);
}
