namespace SatelliteData.Application.Tasks;

public sealed record TaskProcessedDataColumnDto(string ParamId, string Label);

public sealed record TaskProcessedDataCellDto(
    double? Value,
    bool IsOutlier,
    bool IsConfirmedOutlier = false,
    string? ReviewStatus = null);

public sealed record TaskProcessedDataRowDto(string Ts, IReadOnlyDictionary<string, TaskProcessedDataCellDto> Cells);

public sealed record TaskProcessedDataDto(
    Guid RunId,
    IReadOnlyList<TaskProcessedDataColumnDto> Columns,
    IReadOnlyList<TaskProcessedDataRowDto> Rows,
    long Total,
    int Page,
    int PageSize);

/// <summary>单条离群点（时刻 + 数值 + 判定方法）。</summary>
public sealed record TaskOutlierPointItemDto(
    Guid ReviewId,
    string ParamId,
    string ParamLabel,
    string Ts,
    double Value,
    string OutlierMethod,
    string ReviewStatus,
    string? Remark);

public sealed record TaskOutlierPointsDto(
    Guid RunId,
    IReadOnlyList<TaskOutlierPointItemDto> Items,
    long Total,
    int Page,
    int PageSize);

/// <summary>单段连续离群时间区间（PostgreSQL <c>preprocess_outlier_segment</c>）。</summary>
public sealed record TaskOutlierSegmentItemDto(
    string ParamId,
    string ParamLabel,
    string SegmentStart,
    string SegmentEnd,
    string OutlierMethod,
    double DurationSeconds,
    string SegmentKind);

public sealed record TaskOutlierSegmentsDto(
    Guid RunId,
    IReadOnlyList<TaskOutlierSegmentItemDto> Items,
    int Total,
    string SegmentKind,
    bool ReviewCompleted);

public sealed record TaskValidRangeItemDto(
    string RangeStart,
    string RangeEnd,
    double DurationSeconds);

public sealed record TaskValidRangesDto(
    Guid RunId,
    IReadOnlyList<TaskValidRangeItemDto> Items,
    int Total);
