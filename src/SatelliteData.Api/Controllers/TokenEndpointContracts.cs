using System.Text.Json.Serialization;

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
    [property: JsonPropertyName("jobId")] string JobId,
    [property: JsonPropertyName("runId")] Guid RunId,
    [property: JsonPropertyName("status")] string Status);

public sealed record JobStatusResponse(
    [property: JsonPropertyName("run_id")] Guid RunId,
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("progress_percent")] decimal ProgressPercent,
    [property: JsonPropertyName("current_step")] string? CurrentStep,
    [property: JsonPropertyName("error_code")] string? ErrorCode,
    [property: JsonPropertyName("error_msg")] string? ErrorMsg);
