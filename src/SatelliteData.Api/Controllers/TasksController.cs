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
        [property: JsonPropertyName("test_batch_name")] string? TestBatchName,
        [property: JsonPropertyName("progress_percent")] decimal ProgressPercent,
        [property: JsonPropertyName("current_step")] string? CurrentStep,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("end_time")] DateTimeOffset? EndTime);

    public sealed record CreatePipelineBody(
        string TasookNo,
        string SatelliteNo,
        string? TestBatchName,
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
            body.TestBatchName,
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
        string? TestBatchName,
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
        if (body.FilterTemplateId is null || body.FilterTemplateVersion is null)
        {
            return StatusCode(
                StatusCodes.Status422UnprocessableEntity,
                ApiResponse<object>.Fail(
                    TaskErrorCodes.FilterTemplateRequired,
                    "请选择筛选模板",
                    HttpContext));
        }

        var cmd = new PreprocessCreateCommand(
            body.TasookNo.Trim(),
            body.SatelliteNo.Trim(),
            body.TestBatchName,
            body.WindowStart,
            body.WindowEnd,
            body.FilterTemplateId.Value,
            body.FilterTemplateVersion.Value,
            body.IdempotencyKey,
            TaskTriggerType.Api);

        try
        {
            var result = await orchestrator.CreatePreprocessAsync(cmd, createdBy: null, cancellationToken);
            return Ok(ApiResponse<AcceptedJobResponse>.Ok(
                new AcceptedJobResponse(result.JobId, result.RunId, result.Status.ToString()),
                HttpContext));
        }
        catch (InvalidTaskWindowException)
        {
            return BadRequest(
                ApiResponse<object>.Fail(InvalidTaskWindowException.Code, "window_start 必须早于 window_end", HttpContext));
        }
        catch (TaskValidationException ex)
        {
            var status = ex.ErrorCode switch
            {
                TaskErrorCodes.SatelliteNotFound => StatusCodes.Status404NotFound,
                TaskErrorCodes.SatelliteDisabled => StatusCodes.Status422UnprocessableEntity,
                TaskErrorCodes.FilterTemplateNotFound => StatusCodes.Status404NotFound,
                TaskErrorCodes.FilterTemplateNotPublished => StatusCodes.Status422UnprocessableEntity,
                TaskErrorCodes.FilterTemplateNotApplicable => StatusCodes.Status422UnprocessableEntity,
                TaskErrorCodes.TasookRequired => StatusCodes.Status422UnprocessableEntity,
                TaskErrorCodes.SatelliteRequired => StatusCodes.Status422UnprocessableEntity,
                TaskErrorCodes.WindowRequired => StatusCodes.Status422UnprocessableEntity,
                TaskErrorCodes.FilterTemplateRequired => StatusCodes.Status422UnprocessableEntity,
                _ => StatusCodes.Status400BadRequest
            };
            return StatusCode(status, ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
    }

    /// <summary>任务列表（按创建时间倒序，默认 50 条，最大 200）。<paramref name="jobType"/> 可选：PIPELINE / PREPROCESS / ALGORITHM。</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TaskRunListItemResponse>>>> List(
        [FromQuery] int pageSize = 50,
        [FromQuery] string? jobType = null,
        CancellationToken cancellationToken = default)
    {
        var runs = await taskRuns.ListRecentAsync(pageSize, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(jobType) && TryParseJobTypeFilter(jobType, out var filter))
        {
            runs = runs.Where(r => r.JobType == filter).ToArray();
        }

        var items = runs.Select(ToListItem).ToArray();
        return Ok(ApiResponse<IReadOnlyList<TaskRunListItemResponse>>.Ok(items, HttpContext));
    }

    [HttpGet("{runId:guid}")]
    public async Task<ActionResult<ApiResponse<TaskRunDetailResponse>>> GetRun(Guid runId, CancellationToken cancellationToken)
    {
        var run = await taskRuns.GetByRunIdAsync(runId, cancellationToken);
        if (run is null)
        {
            return NotFound(ApiResponse<object>.Fail(TaskErrorCodes.NotFound, "任务不存在", HttpContext));
        }

        return Ok(ApiResponse<TaskRunDetailResponse>.Ok(ToDetail(run), HttpContext));
    }

    [HttpPost("{runId:guid}/cancel")]
    public async Task<ActionResult<ApiResponse<AcceptedJobResponse>>> CancelRun(
        Guid runId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await orchestrator.CancelAsync(runId, cancellationToken);
            return Ok(ApiResponse<AcceptedJobResponse>.Ok(
                new AcceptedJobResponse(result.JobId, result.RunId, result.Status.ToString()),
                HttpContext));
        }
        catch (TaskValidationException ex) when (ex.ErrorCode == TaskErrorCodes.NotFound)
        {
            return NotFound(ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
        catch (TaskValidationException ex) when (ex.ErrorCode == TaskErrorCodes.NotCancellable)
        {
            return StatusCode(
                StatusCodes.Status409Conflict,
                ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
    }

    private static TaskRunDetailResponse ToDetail(TaskRun r) =>
        new(
            r.RunId,
            r.JobId,
            JobTypeToApi(r.JobType),
            TriggerTypeToApi(r.TriggerType),
            r.Status.ToString(),
            r.TasookNo,
            r.SatelliteNo,
            r.TestBatchName,
            r.WindowStart,
            r.WindowEnd,
            r.FilterTemplateId,
            r.FilterTemplateVersion,
            r.AlgorithmTemplateId,
            r.AlgorithmTemplateVersion,
            r.ProgressPercent,
            r.CurrentStep,
            r.StartTime,
            r.EndTime,
            r.CreatedAt,
            r.ErrorCode,
            r.ErrorMsg);

    private static TaskRunListItemResponse ToListItem(TaskRun r) =>
        new(
            r.RunId,
            r.JobId,
            JobTypeToApi(r.JobType),
            TriggerTypeToApi(r.TriggerType),
            r.Status.ToString(),
            r.TasookNo,
            r.SatelliteNo,
            r.TestBatchName,
            r.ProgressPercent,
            r.CurrentStep,
            r.CreatedAt,
            r.EndTime);

    private static bool TryParseJobTypeFilter(string raw, out TaskJobType jobType)
    {
        switch (raw.Trim().ToUpperInvariant())
        {
            case "PIPELINE":
                jobType = TaskJobType.Pipeline;
                return true;
            case "PREPROCESS":
                jobType = TaskJobType.Preprocess;
                return true;
            case "ALGORITHM":
                jobType = TaskJobType.Algorithm;
                return true;
            case "WEBHOOK":
                jobType = TaskJobType.Webhook;
                return true;
            default:
                jobType = TaskJobType.Pipeline;
                return false;
        }
    }

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
