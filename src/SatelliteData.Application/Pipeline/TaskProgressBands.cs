namespace SatelliteData.Application.Pipeline;

/// <summary>设计 6.3.4 任务进度区间。</summary>
public static class TaskProgressBands
{
    public const decimal CreatedMax = 5m;
    public const decimal AssetResolveMax = 10m;
    public const decimal PreprocessMax = 60m;
    public const decimal AlgorithmMax = 90m;
    public const decimal WebhookMax = 100m;
}
