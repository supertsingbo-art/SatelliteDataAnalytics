using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using SatelliteData.Application.Tasks;
using SatelliteData.Domain.Tasks;

namespace SatelliteData.Api.Controllers;

[ApiController]
[Route("api/v1/tasks")]
public sealed class TasksController(TaskOrchestrator orchestrator, ITaskRunRepository taskRuns) : ControllerBase
{
    public sealed record TaskRunListItemResponse(
        [property: JsonPropertyName("run_id")] Guid RunId,
        [property: JsonPropertyName("job_id")] string JobId,
        [property: JsonPropertyName("job_type")] string JobType,
        [property: JsonPropertyName("trigger_type")] string TriggerType,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("tasook_no")] string TasookNo,
        [property: JsonPropertyName("satellite_no")] string SatelliteNo,
        [property: JsonPropertyName("test_batch_id")] string? TestBatchId,
        [property: JsonPropertyName("progress_percent")] decimal ProgressPercent,
        [property: JsonPropertyName("current_step")] string? CurrentStep,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("end_time")] DateTimeOffset? EndTime);

    public sealed record CreatePipelineBody(
        string TasookNo,
        string SatelliteNo,
        string? TestBatchId,
        DateTimeOffset? WindowStart,
        DateTimeOffset? WindowEnd,
        Guid? FilterTemplateId,
        int? FilterTemplateVersion,
        Guid? AlgorithmTemplateId,
        int? AlgorithmTemplateVersion,
        string? IdempotencyKey);

    [HttpPost("pipeline")]
    public async Task<ActionResult<ApiResponse<AcceptedJobResponse>>> CreatePipeline(
        [FromBody] CreatePipelineBody body,
        CancellationToken cancellationToken)
    {
        var cmd = new PipelineCreateCommand(
            body.TasookNo,
            body.SatelliteNo,
            body.TestBatchId,
            body.WindowStart,
            body.WindowEnd,
            body.FilterTemplateId ?? PipelineDevIds.DefaultFilterTemplateId,
            body.FilterTemplateVersion ?? 1,
            body.AlgorithmTemplateId ?? PipelineDevIds.DefaultAlgorithmTemplateId,
            body.AlgorithmTemplateVersion ?? 1,
            body.IdempotencyKey,
            TaskTriggerType.Api);

        PipelineCreateResult result;
        try
        {
            result = await orchestrator.CreatePipelineAsync(cmd, createdBy: null, cancellationToken);
        }
        catch (InvalidTaskWindowException)
        {
            return BadRequest(
                ApiResponse<object>.Fail(InvalidTaskWindowException.Code, "window_start 必须早于 window_end", HttpContext));
        }

        return Ok(ApiResponse<AcceptedJobResponse>.Ok(
            new AcceptedJobResponse(result.JobId, result.RunId, result.Status.ToString()),
            HttpContext));
    }

    public sealed record CreatePreprocessBody(
        string TasookNo,
        string SatelliteNo,
        string? TestBatchId,
        DateTimeOffset? WindowStart,
        DateTimeOffset? WindowEnd,
        Guid? FilterTemplateId,
        int? FilterTemplateVersion,
        string? IdempotencyKey);

    [HttpPost("preprocess")]
    public async Task<ActionResult<ApiResponse<AcceptedJobResponse>>> CreatePreprocess(
        [FromBody] CreatePreprocessBody body,
        CancellationToken cancellationToken)
    {
        var cmd = new PreprocessCreateCommand(
            body.TasookNo,
            body.SatelliteNo,
            body.TestBatchId,
            body.WindowStart,
            body.WindowEnd,
            body.FilterTemplateId ?? PipelineDevIds.DefaultFilterTemplateId,
            body.FilterTemplateVersion ?? 1,
            body.IdempotencyKey,
            TaskTriggerType.Api);

        PipelineCreateResult result;
        try
        {
            result = await orchestrator.CreatePreprocessAsync(cmd, createdBy: null, cancellationToken);
        }
        catch (InvalidTaskWindowException)
        {
            return BadRequest(
                ApiResponse<object>.Fail(InvalidTaskWindowException.Code, "window_start 必须早于 window_end", HttpContext));
        }

        return Ok(ApiResponse<AcceptedJobResponse>.Ok(
            new AcceptedJobResponse(result.JobId, result.RunId, result.Status.ToString()),
            HttpContext));
    }

    /// <summary>任务列表（按创建时间倒序，默认 50 条，最大 200）。</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TaskRunListItemResponse>>>> List(
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var runs = await taskRuns.ListRecentAsync(pageSize, cancellationToken).ConfigureAwait(false);
        var items = runs.Select(ToListItem).ToArray();
        return Ok(ApiResponse<IReadOnlyList<TaskRunListItemResponse>>.Ok(items, HttpContext));
    }

    [HttpGet("{runId:guid}")]
    public async Task<ActionResult<ApiResponse<JobStatusResponse>>> GetRun(Guid runId, CancellationToken cancellationToken)
    {
        var run = await taskRuns.GetByRunIdAsync(runId, cancellationToken);
        if (run is null)
        {
            return NotFound(ApiResponse<object>.Fail("TASK_001", "任务不存在", HttpContext));
        }

        return Ok(ApiResponse<JobStatusResponse>.Ok(
            new JobStatusResponse(
                run.RunId,
                run.JobId,
                run.Status.ToString(),
                run.ProgressPercent,
                run.CurrentStep,
                run.ErrorCode,
                run.ErrorMsg),
            HttpContext));
    }

    private static TaskRunListItemResponse ToListItem(TaskRun r) =>
        new(
            r.RunId,
            r.JobId,
            JobTypeToApi(r.JobType),
            TriggerTypeToApi(r.TriggerType),
            r.Status.ToString(),
            r.TasookNo,
            r.SatelliteNo,
            r.TestBatchId,
            r.ProgressPercent,
            r.CurrentStep,
            r.CreatedAt,
            r.EndTime);

    private static string JobTypeToApi(TaskJobType t) =>
        t switch
        {
            TaskJobType.Preprocess => "PREPROCESS",
            TaskJobType.Algorithm => "ALGORITHM",
            TaskJobType.Pipeline => "PIPELINE",
            TaskJobType.Webhook => "WEBHOOK",
            _ => t.ToString().ToUpperInvariant()
        };

    private static string TriggerTypeToApi(TaskTriggerType t) =>
        t switch
        {
            TaskTriggerType.Trial => "TRIAL",
            TaskTriggerType.Scheduled => "SCHEDULED",
            _ => "API"
        };
}
