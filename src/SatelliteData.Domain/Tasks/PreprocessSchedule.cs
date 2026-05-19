namespace SatelliteData.Domain.Tasks;

/// <summary>预处理入仓每日定时计划。</summary>
public sealed record PreprocessSchedule(
    Guid ScheduleId,
    string TasookNo,
    string SatelliteNo,
    Guid FilterTemplateId,
    int FilterTemplateVersion,
    TimeOnly DailyTime,
    int IntervalDays,
    DateOnly EffectiveFrom,
    bool Enabled,
    string HangfireRecurringId,
    Guid? LastRunId,
    string? LastRunStatus,
    DateTimeOffset? LastRunEndAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
