namespace SatelliteData.Domain.Tasks;

/// <summary>任务级离群复核状态。</summary>
public static class OutlierReviewRunStatus
{
    public const string NotRequired = "NOT_REQUIRED";
    public const string Pending = "PENDING";
    public const string Completed = "COMPLETED";
}

/// <summary>单点离群复核状态。</summary>
public static class OutlierReviewPointStatus
{
    public const string Pending = "PENDING";
    public const string Confirmed = "CONFIRMED";
    public const string Jitter = "JITTER";
}

/// <summary>离群时间段来源。</summary>
public static class OutlierSegmentKind
{
    public const string Auto = "AUTO";
    public const string Confirmed = "CONFIRMED";
}

/// <summary>预处理候选离群点人工复核记录。</summary>
public sealed record PreprocessOutlierPointReview(
    Guid ReviewId,
    Guid RunId,
    string TasookNo,
    string SatelliteNo,
    string ParamId,
    DateTimeOffset Ts,
    double? AutoValue,
    string? AutoOutlierMethod,
    string ReviewStatus,
    DateTimeOffset? ReviewedAt,
    string? ReviewedBy,
    string? Remark,
    DateTimeOffset CreatedAt);
