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
    [property: JsonPropertyName("window_end")] DateTimeOffset? WindowEnd);

public sealed record AcceptedJobResponse(
    [property: JsonPropertyName("jobId")] string JobId,
    [property: JsonPropertyName("runId")] Guid RunId,
    [property: JsonPropertyName("status")] string Status);
