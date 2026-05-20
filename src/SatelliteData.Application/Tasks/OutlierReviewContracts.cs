namespace SatelliteData.Application.Tasks;

public sealed record OutlierReviewSummaryDto(
    Guid RunId,
    string? OutlierReviewStatus,
    int AutoCount,
    int PendingCount,
    int ConfirmedCount,
    int JitterCount);

public sealed record OutlierReviewItemDto(
    Guid ReviewId,
    string ParamId,
    string ParamLabel,
    string Ts,
    double? Value,
    string OutlierMethod,
    string ReviewStatus,
    string? Remark);

public sealed record OutlierReviewListDto(
    Guid RunId,
    IReadOnlyList<OutlierReviewItemDto> Items,
    long Total,
    int Page,
    int PageSize);

public sealed record SubmitOutlierReviewItemDto(string ParamId, string Ts, string Status, string? Remark);

public sealed record SubmitOutlierReviewsDto(IReadOnlyList<SubmitOutlierReviewItemDto> Items);

public sealed record CompleteOutlierReviewResultDto(
    Guid RunId,
    string OutlierReviewStatus,
    int ConfirmedSegmentCount);
