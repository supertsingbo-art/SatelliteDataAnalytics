namespace SatelliteData.Application.Tasks;

/// <summary>
/// 曲线视图时间桶宽度：<c>max(1, ceil((windowEnd - windowStart) / maxPoints))</c> 秒。
/// </summary>
public static class ProcessedSeriesBucketCalculator
{
    public static int ComputeBucketSeconds(DateTimeOffset windowStart, DateTimeOffset windowEnd, int maxPoints)
    {
        var safeMaxPoints = Math.Max(1, maxPoints);
        var windowSeconds = (windowEnd - windowStart).TotalSeconds;
        if (windowSeconds <= 0)
        {
            return 1;
        }

        return Math.Max(1, (int)Math.Ceiling(windowSeconds / safeMaxPoints));
    }
}
