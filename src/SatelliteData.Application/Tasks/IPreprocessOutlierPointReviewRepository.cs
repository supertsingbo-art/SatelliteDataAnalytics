using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

public interface IPreprocessOutlierPointReviewRepository
{
    Task InsertBatchAsync(IReadOnlyList<PreprocessOutlierPointReview> reviews, CancellationToken cancellationToken);

    Task<(IReadOnlyList<PreprocessOutlierPointReview> Items, long Total)> ListPageAsync(
        Guid runId,
        string? statusFilter,
        string? paramIdFilter,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PreprocessOutlierPointReview>> ListByRunIdAndStatusAsync(
        Guid runId,
        string status,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PreprocessOutlierPointReview>> ListByRunIdAsync(
        Guid runId,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, int>> CountByStatusAsync(Guid runId, CancellationToken cancellationToken);

    Task<bool> UpdateStatusBatchAsync(
        Guid runId,
        IReadOnlyList<OutlierReviewUpdate> updates,
        DateTimeOffset reviewedAt,
        string? reviewedBy,
        CancellationToken cancellationToken);

    Task DeleteByRunIdAsync(Guid runId, CancellationToken cancellationToken);
}

public sealed record OutlierReviewUpdate(
    string ParamId,
    DateTimeOffset Ts,
    string Status,
    string? Remark);
