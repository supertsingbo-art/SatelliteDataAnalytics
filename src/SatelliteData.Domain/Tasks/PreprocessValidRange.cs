namespace SatelliteData.Domain.Tasks;

/// <summary>预处理阶段依据有效时间段提取规则得到的有效时间段。</summary>
public sealed record PreprocessValidRange(
    Guid RangeId,
    Guid RunId,
    string TasookNo,
    string SatelliteNo,
    DateTimeOffset RangeStart,
    DateTimeOffset RangeEnd,
    DateTimeOffset CreatedAt);
