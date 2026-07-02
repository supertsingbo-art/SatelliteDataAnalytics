using SatelliteData.Application.Pipeline;
using SatelliteData.Application.Tasks;
using SatelliteData.Domain.Tasks;

namespace SatelliteData.Infrastructure.Pipeline;

public sealed class InMemoryTaskRunRepository : ITaskRunRepository
{
    private readonly Dictionary<Guid, TaskRun> _byId = [];
    private readonly Dictionary<string, TaskRun> _byIdem = [];
    private readonly object _gate = new();

    public Task<TaskRun?> GetByRunIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _byId.TryGetValue(runId, out var r);
            return Task.FromResult(r);
        }
    }

    public Task<TaskRun?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _byIdem.TryGetValue(idempotencyKey, out var r);
            return Task.FromResult(r);
        }
    }

    public Task InsertAsync(TaskRun run, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _byId[run.RunId] = run;
            _byIdem[run.IdempotencyKey] = run;
            return Task.CompletedTask;
        }
    }

    public Task UpdateAsync(TaskRun run, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _byId[run.RunId] = run;
            if (_byIdem.TryGetValue(run.IdempotencyKey, out var existing) && existing.RunId == run.RunId)
            {
                _byIdem[run.IdempotencyKey] = run;
            }

            return Task.CompletedTask;
        }
    }

    public Task<bool> UpdateIfNotCancelledAsync(TaskRun run, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            if (_byId.TryGetValue(run.RunId, out var existing) && existing.Status == TaskRunStatus.Cancelled)
            {
                return Task.FromResult(false);
            }

            _byId[run.RunId] = run;
            if (_byIdem.TryGetValue(run.IdempotencyKey, out var idem) && idem.RunId == run.RunId)
            {
                _byIdem[run.IdempotencyKey] = run;
            }

            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<TaskRun>> ListRecentAsync(int limit, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            var cap = Math.Clamp(limit, 1, 200);
            var arr = _byId.Values
                .OrderByDescending(r => r.CreatedAt)
                .Take(cap)
                .ToArray();
            return Task.FromResult<IReadOnlyList<TaskRun>>(arr);
        }
    }

    public Task<IReadOnlyList<TaskRun>> ListByScheduleIdAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            var arr = _byId.Values
                .Where(r => r.ScheduleId == scheduleId)
                .OrderByDescending(r => r.CreatedAt)
                .ToArray();
            return Task.FromResult<IReadOnlyList<TaskRun>>(arr);
        }
    }

    public Task<TaskRun?> GetLatestByScheduleIdAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            var latest = _byId.Values
                .Where(r => r.ScheduleId == scheduleId)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefault();
            return Task.FromResult(latest);
        }
    }

    public Task DeleteAsync(Guid runId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            if (_byId.Remove(runId, out var removed))
            {
                var idemKey = removed.IdempotencyKey;
                if (_byIdem.TryGetValue(idemKey, out var cur) && cur.RunId == runId)
                {
                    _byIdem.Remove(idemKey);
                }
            }

            return Task.CompletedTask;
        }
    }
}

public sealed class InMemoryTaskEventRepository : ITaskEventRepository
{
    private readonly List<TaskEvent> _events = [];
    private readonly object _gate = new();

    public Task AppendAsync(TaskEvent evt, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _events.Add(evt);
            return Task.CompletedTask;
        }
    }

    public Task DeleteByRunIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            _events.RemoveAll(e => e.RunId == runId);
            return Task.CompletedTask;
        }
    }
}

public sealed class InMemoryHqParamMetadataRepository : IHqParamMetadataRepository
{
    private readonly List<HqParamMetadataRow> _rows = [];
    private readonly object _gate = new();

    public Task InsertAsync(HqParamMetadataRow row, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            _rows.Add(row);
            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<HqParamMetadataRow>> ListByRunIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<HqParamMetadataRow>>(_rows.Where(r => r.RunId == runId).ToArray());
        }
    }

    public Task DeleteByRunIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            _rows.RemoveAll(r => r.RunId == runId);
            return Task.CompletedTask;
        }
    }
}

public sealed class InMemoryPreprocessParamClaimRepository : IPreprocessParamClaimRepository
{
    private readonly List<InMemoryClaimRow> _rows = [];
    private readonly object _gate = new();

    public Task<PreprocessParamClaimAcquireResult> TryAcquireAsync(
        Guid runId,
        string tasookNo,
        string satelliteNo,
        Guid filterTemplateId,
        int filterTemplateVersion,
        IReadOnlyList<PreprocessParamClaimRequest> claims,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (claims.Count == 0)
        {
            return Task.FromResult(PreprocessParamClaimAcquireResult.Success);
        }

        lock (_gate)
        {
            var normalized = claims
                .Where(c => !string.IsNullOrWhiteSpace(c.ParamId) && c.SegmentStart < c.SegmentEnd)
                .Select(c => c with { ParamId = c.ParamId.Trim() })
                .ToList();

            var conflictParams = new HashSet<string>(StringComparer.Ordinal);
            PreprocessParamClaimConflict? firstConflict = null;
            foreach (var claim in normalized)
            {
                var conflict = _rows.FirstOrDefault(row =>
                    row.RunId != runId
                    && string.Equals(row.TasookNo, tasookNo, StringComparison.Ordinal)
                    && string.Equals(row.SatelliteNo, satelliteNo, StringComparison.Ordinal)
                    && string.Equals(row.ParamId, claim.ParamId, StringComparison.Ordinal)
                    && row.Status is ClaimStatus.Active or ClaimStatus.Committed
                    && claim.SegmentStart < row.SegmentEnd
                    && claim.SegmentEnd > row.SegmentStart);
                if (conflict is null)
                {
                    continue;
                }

                conflictParams.Add(claim.ParamId);
                firstConflict ??= new PreprocessParamClaimConflict(
                    claim.ParamId,
                    conflict.RunId,
                    conflict.FilterTemplateId,
                    conflict.FilterTemplateVersion);
            }

            if (conflictParams.Count > 0)
            {
                return Task.FromResult(
                    PreprocessParamClaimAcquireResult.Conflict(
                        conflictParams.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                        firstConflict));
            }

            foreach (var claim in normalized)
            {
                _rows.Add(new InMemoryClaimRow(
                    Guid.NewGuid(),
                    runId,
                    tasookNo,
                    satelliteNo,
                    claim.ParamId,
                    claim.SegmentStart,
                    claim.SegmentEnd,
                    filterTemplateId,
                    filterTemplateVersion,
                    ClaimStatus.Active,
                    DateTimeOffset.UtcNow));
            }

            return Task.FromResult(PreprocessParamClaimAcquireResult.Success);
        }
    }

    public Task MarkCommittedByRunIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                if (row.RunId == runId && row.Status == ClaimStatus.Active)
                {
                    _rows[i] = row with { Status = ClaimStatus.Committed };
                }
            }

            return Task.CompletedTask;
        }
    }

    public Task ReleaseActiveByRunIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            _rows.RemoveAll(row => row.RunId == runId && row.Status == ClaimStatus.Active);
            return Task.CompletedTask;
        }
    }

    public Task DeleteByRunIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            _rows.RemoveAll(row => row.RunId == runId);
            return Task.CompletedTask;
        }
    }

    private enum ClaimStatus
    {
        Active = 0,
        Committed = 1
    }

    private sealed record InMemoryClaimRow(
        Guid ClaimId,
        Guid RunId,
        string TasookNo,
        string SatelliteNo,
        string ParamId,
        DateTimeOffset SegmentStart,
        DateTimeOffset SegmentEnd,
        Guid FilterTemplateId,
        int FilterTemplateVersion,
        ClaimStatus Status,
        DateTimeOffset CreatedAt);
}

public sealed class InMemoryPreprocessScheduleRepository : IPreprocessScheduleRepository
{
    private readonly Dictionary<Guid, PreprocessSchedule> _byId = [];
    private readonly object _gate = new();

    public Task<PreprocessSchedule?> GetByIdAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _byId.TryGetValue(scheduleId, out var s);
            return Task.FromResult(s);
        }
    }

    public Task InsertAsync(PreprocessSchedule schedule, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _byId[schedule.ScheduleId] = schedule;
            return Task.CompletedTask;
        }
    }

    public Task UpdateAsync(PreprocessSchedule schedule, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _byId[schedule.ScheduleId] = schedule;
            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<PreprocessSchedule>> ListEnabledAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            var arr = _byId.Values.Where(s => s.Enabled).OrderByDescending(s => s.CreatedAt).ToArray();
            return Task.FromResult<IReadOnlyList<PreprocessSchedule>>(arr);
        }
    }
}

public sealed class InMemoryPreprocessOutlierSegmentRepository : IPreprocessOutlierSegmentRepository
{
    private readonly List<PreprocessOutlierSegment> _segments = [];
    private readonly object _gate = new();

    public Task InsertBatchAsync(IReadOnlyList<PreprocessOutlierSegment> segments, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            _segments.AddRange(segments);
            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<PreprocessOutlierSegment>> ListByRunIdAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            var arr = _segments.Where(s => s.RunId == runId).OrderBy(s => s.SegmentStart).ToArray();
            return Task.FromResult<IReadOnlyList<PreprocessOutlierSegment>>(arr);
        }
    }

    public Task<IReadOnlyList<PreprocessOutlierSegment>> ListByRunIdAndKindAsync(
        Guid runId,
        string segmentKind,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            var arr = _segments
                .Where(s => s.RunId == runId && string.Equals(s.SegmentKind, segmentKind, StringComparison.Ordinal))
                .OrderBy(s => s.SegmentStart)
                .ToArray();
            return Task.FromResult<IReadOnlyList<PreprocessOutlierSegment>>(arr);
        }
    }

    public Task DeleteByRunIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            _segments.RemoveAll(s => s.RunId == runId);
            return Task.CompletedTask;
        }
    }

    public Task DeleteByRunIdAndKindAsync(Guid runId, string segmentKind, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            _segments.RemoveAll(s => s.RunId == runId && string.Equals(s.SegmentKind, segmentKind, StringComparison.Ordinal));
            return Task.CompletedTask;
        }
    }
}

public sealed class InMemoryPreprocessOutlierPointReviewRepository : IPreprocessOutlierPointReviewRepository
{
    private readonly List<PreprocessOutlierPointReview> _reviews = [];
    private readonly object _gate = new();

    public Task InsertBatchAsync(IReadOnlyList<PreprocessOutlierPointReview> reviews, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            _reviews.AddRange(reviews);
            return Task.CompletedTask;
        }
    }

    public Task<(IReadOnlyList<PreprocessOutlierPointReview> Items, long Total)> ListPageAsync(
        Guid runId,
        string? statusFilter,
        string? paramIdFilter,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            var q = _reviews.Where(r => r.RunId == runId).AsEnumerable();
            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                q = q.Where(r => string.Equals(r.ReviewStatus, statusFilter.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(paramIdFilter))
            {
                q = q.Where(r => string.Equals(r.ParamId, paramIdFilter.Trim(), StringComparison.Ordinal));
            }

            var ordered = q.OrderBy(r => r.Ts).ThenBy(r => r.ParamId, StringComparer.Ordinal).ToArray();
            var total = ordered.LongLength;
            var safePage = Math.Max(1, page);
            var safePageSize = Math.Clamp(pageSize, 1, 200);
            var items = ordered.Skip((safePage - 1) * safePageSize).Take(safePageSize).ToArray();
            return Task.FromResult<(IReadOnlyList<PreprocessOutlierPointReview> Items, long Total)>((items, total));
        }
    }

    public Task<IReadOnlyList<PreprocessOutlierPointReview>> ListByRunIdAndStatusAsync(
        Guid runId,
        string status,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            var arr = _reviews
                .Where(r => r.RunId == runId && string.Equals(r.ReviewStatus, status, StringComparison.Ordinal))
                .OrderBy(r => r.ParamId, StringComparer.Ordinal)
                .ThenBy(r => r.Ts)
                .ToArray();
            return Task.FromResult<IReadOnlyList<PreprocessOutlierPointReview>>(arr);
        }
    }

    public Task<IReadOnlyList<PreprocessOutlierPointReview>> ListByRunIdAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            var arr = _reviews
                .Where(r => r.RunId == runId)
                .OrderBy(r => r.ParamId, StringComparer.Ordinal)
                .ThenBy(r => r.Ts)
                .ToArray();
            return Task.FromResult<IReadOnlyList<PreprocessOutlierPointReview>>(arr);
        }
    }

    public Task<IReadOnlyDictionary<string, int>> CountByStatusAsync(Guid runId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            var dict = _reviews
                .Where(r => r.RunId == runId)
                .GroupBy(r => r.ReviewStatus, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            return Task.FromResult<IReadOnlyDictionary<string, int>>(dict);
        }
    }

    public Task<bool> UpdateStatusBatchAsync(
        Guid runId,
        IReadOnlyList<OutlierReviewUpdate> updates,
        DateTimeOffset reviewedAt,
        string? reviewedBy,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            var changed = false;
            foreach (var u in updates)
            {
                var idx = _reviews.FindIndex(r =>
                    r.RunId == runId
                    && string.Equals(r.ParamId, u.ParamId, StringComparison.Ordinal)
                    && r.Ts == u.Ts
                    && string.Equals(r.ReviewStatus, OutlierReviewPointStatus.Pending, StringComparison.Ordinal));
                if (idx < 0) continue;
                var old = _reviews[idx];
                _reviews[idx] = old with
                {
                    ReviewStatus = u.Status,
                    ReviewedAt = reviewedAt,
                    ReviewedBy = reviewedBy,
                    Remark = u.Remark
                };
                changed = true;
            }

            return Task.FromResult(changed);
        }
    }

    public Task DeleteByRunIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_gate)
        {
            _reviews.RemoveAll(r => r.RunId == runId);
            return Task.CompletedTask;
        }
    }
}

public sealed class InMemoryClientCallbackRepository : IClientCallbackRepository
{
    public Task<IReadOnlyList<ClientCallbackRow>> GetEnabledCallbacksAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ClientCallbackRow>>(Array.Empty<ClientCallbackRow>());

    public Task InsertDeliveryAsync(
        Guid deliveryId,
        string eventId,
        Guid callbackId,
        Guid? runId,
        string eventType,
        string payloadJson,
        string status,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public Task UpdateDeliveryAsync(
        Guid deliveryId,
        string status,
        int responseStatus,
        string? responseBody,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
