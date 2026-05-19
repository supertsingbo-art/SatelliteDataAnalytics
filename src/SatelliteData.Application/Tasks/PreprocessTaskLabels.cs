namespace SatelliteData.Application.Tasks;

/// <summary>预处理入仓任务在 <c>task_run.test_batch_name</c> 中使用的展示标签（非外键）。</summary>
public static class PreprocessTaskLabels
{
    /// <summary>用户选择「自定义时间」时写入 task_run，实际处理以 window_start / window_end 为准。</summary>
    public const string CustomTimeWindowDisplayName = "自定义时间段";

    public static bool IsCustomTimeWindowLabel(string? testBatchName) =>
        string.Equals(testBatchName?.Trim(), CustomTimeWindowDisplayName, StringComparison.Ordinal);
}
