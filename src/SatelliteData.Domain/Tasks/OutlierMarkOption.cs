namespace SatelliteData.Domain.Tasks;

/// <summary>系统级离群标记配置项。</summary>
public sealed record OutlierMarkOption(
    string MarkCode,
    string MarkLabel,
    bool IsOutlier,
    int SortOrder,
    bool Enabled);
