namespace SatelliteData.Application.Tasks;

public sealed class TaskValidationException(string errorCode, string message) : InvalidOperationException(message)
{
    public string ErrorCode { get; } = errorCode;
}

public static class TaskErrorCodes
{
    public const string SatelliteNotFound = "ASSET_SATELLITE_NOT_FOUND";
    public const string SatelliteDisabled = "ASSET_SATELLITE_DISABLED";
    public const string FilterTemplateNotFound = "TPL_001";
    public const string FilterTemplateNotPublished = "TPL_003";
    public const string FilterTemplateNotApplicable = "TPL_006";
    public const string TasookRequired = "TASK_TASOOK_REQUIRED";
    public const string SatelliteRequired = "TASK_SATELLITE_REQUIRED";
    public const string WindowRequired = "TASK_WINDOW_REQUIRED";
    public const string FilterTemplateRequired = "TASK_FILTER_TEMPLATE_REQUIRED";
    public const string NotFound = "TASK_001";
    public const string NotCancellable = "TASK_NOT_CANCELLABLE";
    public const string ExecutionModeInvalid = "TASK_EXECUTION_MODE_INVALID";
    public const string ScheduleTimeRequired = "TASK_SCHEDULE_TIME_REQUIRED";
    public const string ScheduleTimeInvalid = "TASK_SCHEDULE_TIME_INVALID";
    public const string DailyTimeRequired = "TASK_DAILY_TIME_REQUIRED";
    public const string EffectiveFromRequired = "TASK_EFFECTIVE_FROM_REQUIRED";
    public const string IntervalDaysInvalid = "TASK_INTERVAL_DAYS_INVALID";
}
