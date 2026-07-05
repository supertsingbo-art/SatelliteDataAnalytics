using System.Text.Json.Serialization;
using SatelliteData.Application.Tasks;

namespace SatelliteData.Api.Controllers;

public sealed record TokenEndpointResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("scope")] string Scope);

public sealed record OAuthErrorContract(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("error_description")] string ErrorDescription);

public sealed record CreatePipelineJobRequest(
    [property: JsonPropertyName("tasook_no")] string TasookNo,
    [property: JsonPropertyName("satellite_no")] string SatelliteNo,
    [property: JsonPropertyName("test_batch_id")] string? TestBatchId,
    [property: JsonPropertyName("window_start")] DateTimeOffset? WindowStart,
    [property: JsonPropertyName("window_end")] DateTimeOffset? WindowEnd,
    [property: JsonPropertyName("use_filter_template")] bool? UseFilterTemplate,
    [property: JsonPropertyName("filter_template_id")] Guid? FilterTemplateId,
    [property: JsonPropertyName("filter_template_version")] int? FilterTemplateVersion,
    [property: JsonPropertyName("algorithm_template_id")] Guid? AlgorithmTemplateId,
    [property: JsonPropertyName("algorithm_template_version")] int? AlgorithmTemplateVersion,
    [property: JsonPropertyName("idempotency_key")] string? IdempotencyKey,
    [property: JsonPropertyName("trigger")] string? Trigger);

public sealed record CreatePreprocessJobRequest(
    [property: JsonPropertyName("tasook_no")] string TasookNo,
    [property: JsonPropertyName("satellite_no")] string SatelliteNo,
    [property: JsonPropertyName("test_batch_id")] string? TestBatchId,
    [property: JsonPropertyName("window_start")] DateTimeOffset? WindowStart,
    [property: JsonPropertyName("window_end")] DateTimeOffset? WindowEnd,
    [property: JsonPropertyName("filter_template_id")] Guid? FilterTemplateId,
    [property: JsonPropertyName("filter_template_version")] int? FilterTemplateVersion,
    [property: JsonPropertyName("idempotency_key")] string? IdempotencyKey,
    [property: JsonPropertyName("trigger")] string? Trigger);

public sealed record AcceptedJobResponse(
    [property: JsonPropertyName("jobId")] string? JobId,
    [property: JsonPropertyName("runId")] Guid? RunId,
    [property: JsonPropertyName("scheduleId")] Guid? ScheduleId,
    [property: JsonPropertyName("status")] string Status);

public sealed record JobStatusResponse(
    [property: JsonPropertyName("run_id")] Guid RunId,
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("progress_percent")] decimal ProgressPercent,
    [property: JsonPropertyName("current_step")] string? CurrentStep,
    [property: JsonPropertyName("error_code")] string? ErrorCode,
    [property: JsonPropertyName("error_msg")] string? ErrorMsg);

/// <summary>任务详情（管理端 GET /api/v1/tasks/{runId}）。</summary>
public sealed record TaskRunDetailResponse(
    [property: JsonPropertyName("run_id")] Guid RunId,
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("job_type")] string JobType,
    [property: JsonPropertyName("trigger_type")] string TriggerType,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("tasook_no")] string TasookNo,
    [property: JsonPropertyName("satellite_no")] string SatelliteNo,
    [property: JsonPropertyName("test_batch_name")] string? TestBatchName,
    [property: JsonPropertyName("window_start")] DateTimeOffset? WindowStart,
    [property: JsonPropertyName("window_end")] DateTimeOffset? WindowEnd,
    [property: JsonPropertyName("filter_template_id")] Guid? FilterTemplateId,
    [property: JsonPropertyName("filter_template_version")] int? FilterTemplateVersion,
    [property: JsonPropertyName("filter_template_name")] string? FilterTemplateName,
    [property: JsonPropertyName("algorithm_template_id")] Guid? AlgorithmTemplateId,
    [property: JsonPropertyName("algorithm_template_version")] int? AlgorithmTemplateVersion,
    [property: JsonPropertyName("algorithm_template_name")] string? AlgorithmTemplateName,
    [property: JsonPropertyName("progress_percent")] decimal ProgressPercent,
    [property: JsonPropertyName("current_step")] string? CurrentStep,
    [property: JsonPropertyName("start_time")] DateTimeOffset? StartTime,
    [property: JsonPropertyName("end_time")] DateTimeOffset? EndTime,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("error_code")] string? ErrorCode,
    [property: JsonPropertyName("error_msg")] string? ErrorMsg,
    [property: JsonPropertyName("execution_mode")] string? ExecutionMode,
    [property: JsonPropertyName("scheduled_at")] DateTimeOffset? ScheduledAt,
    [property: JsonPropertyName("schedule_id")] Guid? ScheduleId,
    [property: JsonPropertyName("schedule_daily_time")] string? ScheduleDailyTime,
    [property: JsonPropertyName("schedule_interval_days")] int? ScheduleIntervalDays,
    [property: JsonPropertyName("schedule_effective_from")] DateOnly? ScheduleEffectiveFrom,
    [property: JsonPropertyName("conflict_details")] IReadOnlyList<PreprocessConflictDetailDto>? ConflictDetails);
