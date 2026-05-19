namespace SatelliteData.Domain.Tasks;

/// <summary>任务类型。详见设计 6.3.1。</summary>
public enum TaskJobType
{
    Preprocess,
    Algorithm,
    Pipeline,
    Webhook
}

/// <summary>触发类型。</summary>
public enum TaskTriggerType
{
    Api,
    Trial,
    Scheduled
}

/// <summary>任务状态。详见设计 6.3.2。</summary>
public enum TaskRunStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Timeout,
    Cancelled
}

public sealed record TaskRun(
    Guid RunId,
    Guid? ParentRunId,
    string JobId,
    TaskJobType JobType,
    TaskTriggerType TriggerType,
    TaskRunStatus Status,
    string IdempotencyKey,
    string TasookNo,
    string SatelliteNo,
    string? TestBatchId,
    /// <summary>测试阶段名称（<c>test_batch_cache.scenario</c>）；管线按时间窗推断阶段时回写。</summary>
    string? TestPhaseScenario,
    DateTimeOffset? WindowStart,
    DateTimeOffset? WindowEnd,
    Guid? FilterTemplateId,
    int? FilterTemplateVersion,
    Guid? AlgorithmTemplateId,
    int? AlgorithmTemplateVersion,
    Guid? ReportTemplateId,
    int? ReportTemplateVersion,
    decimal ProgressPercent,
    string? CurrentStep,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    bool TimeoutFlag,
    string? ErrorCode,
    string? ErrorMsg,
    Guid? CreatedBy,
    DateTimeOffset CreatedAt);

public sealed record TaskEvent(
    Guid EventId,
    Guid RunId,
    string EventType,
    string EventStatus,
    string? PayloadJson,
    string? ErrorCode,
    string? ErrorMsg,
    DateTimeOffset CreatedAt);
