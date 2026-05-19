namespace SatelliteData.Domain.Tasks;

/// <summary>预处理执行中某参数的离群时间段。</summary>
public sealed record PreprocessOutlierSegment(
    Guid SegmentId,
    Guid RunId,
    string TasookNo,
    string SatelliteNo,
    string ParamId,
    DateTimeOffset SegmentStart,
    DateTimeOffset SegmentEnd,
    string OutlierMethod,
    DateTimeOffset CreatedAt);
