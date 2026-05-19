using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

public sealed record TaskListItemDto(
    string ItemType,
    Guid ItemId,
    Guid? RunId,
    Guid? ScheduleId,
    string? JobId,
    string JobType,
    string? ExecutionMode,
    bool CanExecute,
    string DisplayStatus,
    string Status,
    string TasookNo,
    string SatelliteNo,
    string? TestBatchName,
    decimal ProgressPercent,
    string? CurrentStep,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EndTime);

public sealed record TaskExecutionRecordDto(
    Guid RunId,
    string? JobId,
    string Status,
    string DisplayStatus,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    DateTimeOffset? WindowStart,
    DateTimeOffset? WindowEnd,
    string? ErrorCode,
    string? ErrorMsg);

public sealed record ExecuteTaskResultDto(
    string DisplayStatus,
    Guid? RunId,
    Guid? ScheduleId,
    string? JobId,
    string Status);
