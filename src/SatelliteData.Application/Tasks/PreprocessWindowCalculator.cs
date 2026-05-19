namespace SatelliteData.Application.Tasks;

/// <summary>按服务器本地时区计算每日定时任务的数据时间窗。</summary>
public static class PreprocessWindowCalculator
{
    /// <summary>
    /// 给定参考本地日期与每日时刻，返回 [前一日同一时刻, 当日该时刻) 对应的 UTC 偏移时间窗。
    /// </summary>
    public static (DateTimeOffset WindowStart, DateTimeOffset WindowEnd) ComputeDailyWindow(
        DateOnly referenceLocalDate,
        TimeOnly dailyTime)
    {
        var windowEndLocal = referenceLocalDate.ToDateTime(dailyTime);
        var windowStartLocal = windowEndLocal.AddDays(-1);
        var offset = TimeZoneInfo.Local.GetUtcOffset(windowEndLocal);
        return (
            new DateTimeOffset(windowStartLocal, offset),
            new DateTimeOffset(windowEndLocal, offset));
    }

    /// <summary>是否应在今日触发（考虑生效日与间隔天数）。</summary>
    public static bool ShouldRunToday(DateOnly effectiveFrom, int intervalDays, DateOnly todayLocal)
    {
        if (intervalDays < 1) intervalDays = 1;
        if (todayLocal < effectiveFrom) return false;
        var days = todayLocal.DayNumber - effectiveFrom.DayNumber;
        return days % intervalDays == 0;
    }

    /// <summary>构建 Hangfire Cron（秒 分 时 * * *，本地时刻）。</summary>
    public static string BuildDailyCron(TimeOnly dailyTime) =>
        $"{dailyTime.Second} {dailyTime.Minute} {dailyTime.Hour} * * *";

    public static string BuildRecurringJobId(Guid scheduleId) => $"preprocess-schedule-{scheduleId:N}";
}
