namespace SatelliteData.Domain.Tasks;

/// <summary>预处理入仓任务执行模式。</summary>
public enum PreprocessExecutionMode
{
    /// <summary>创建后立即执行。</summary>
    Immediate,

    /// <summary>在指定时刻执行一次。</summary>
    OnceScheduled,

    /// <summary>由每日计划触发的一次执行实例。</summary>
    DailyInstance
}
