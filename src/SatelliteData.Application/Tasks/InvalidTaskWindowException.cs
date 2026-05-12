namespace SatelliteData.Application.Tasks;

/// <summary>任务时间窗非法：同时给出起止时间时要求 window_start &lt; window_end。</summary>
public sealed class InvalidTaskWindowException : Exception
{
    public const string Code = "TASK_WINDOW_INVALID";

    public InvalidTaskWindowException()
        : base("window_start 必须早于 window_end")
    {
    }
}
