using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SatelliteData.Application.Tasks;
using SatelliteData.Application.Templates;
using SatelliteData.Domain.Tasks;

namespace SatelliteData.Api.Controllers;

[ApiController]
[Route("api/v1/tasks")]
public sealed class TasksController(
    TaskOrchestrator orchestrator,
    PreprocessScheduleService scheduleService,
    TaskListService taskListService,
    TaskExecutionService taskExecutionService,
    TaskRunLifecycleService taskLifecycleService,
    TaskRunProcessedDataService taskProcessedDataService,
    OutlierReviewService outlierReviewService,
    PreprocessConflictReader conflictReader,
    ITaskRunRepository taskRuns,
    IPreprocessScheduleRepository scheduleRepository,
    IFilterTemplateRepository filterTemplates,
    IAlgorithmTemplateRepository algorithmTemplates) : ControllerBase
{
    public sealed record TaskListItemResponse(
        [property: JsonPropertyName("item_type")] string ItemType,
        [property: JsonPropertyName("item_id")] Guid ItemId,
        [property: JsonPropertyName("run_id")] Guid? RunId,
        [property: JsonPropertyName("schedule_id")] Guid? ScheduleId,
        [property: JsonPropertyName("job_id")] string? JobId,
        [property: JsonPropertyName("job_type")] string JobType,
        [property: JsonPropertyName("execution_mode")] string? ExecutionMode,
        [property: JsonPropertyName("can_execute")] bool CanExecute,
        [property: JsonPropertyName("can_delete")] bool CanDelete,
        [property: JsonPropertyName("can_re_execute")] bool CanReExecute,
        [property: JsonPropertyName("can_view_data")] bool CanViewData,
        [property: JsonPropertyName("outlier_pending_count")] int OutlierPendingCount,
        [property: JsonPropertyName("outlier_review_status")] string? OutlierReviewStatus,
        [property: JsonPropertyName("status_summary")] string StatusSummary,
        [property: JsonPropertyName("display_status")] string DisplayStatus,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("tasook_no")] string TasookNo,
        [property: JsonPropertyName("satellite_no")] string SatelliteNo,
        [property: JsonPropertyName("test_batch_name")] string? TestBatchName,
        [property: JsonPropertyName("progress_percent")] decimal ProgressPercent,
        [property: JsonPropertyName("current_step")] string? CurrentStep,
        [property: JsonPropertyName("scheduled_at")] DateTimeOffset? ScheduledAt,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("end_time")] DateTimeOffset? EndTime,
        [property: JsonPropertyName("error_code")] string? ErrorCode,
        [property: JsonPropertyName("error_msg")] string? ErrorMsg);

    public sealed record TaskExecutionRecordResponse(
        [property: JsonPropertyName("run_id")] Guid RunId,
        [property: JsonPropertyName("job_id")] string? JobId,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("display_status")] string DisplayStatus,
        [property: JsonPropertyName("started_at")] DateTimeOffset? StartedAt,
        [property: JsonPropertyName("ended_at")] DateTimeOffset? EndedAt,
        [property: JsonPropertyName("window_start")] DateTimeOffset? WindowStart,
        [property: JsonPropertyName("window_end")] DateTimeOffset? WindowEnd,
        [property: JsonPropertyName("error_code")] string? ErrorCode,
        [property: JsonPropertyName("error_msg")] string? ErrorMsg,
        [property: JsonPropertyName("conflict_details")] IReadOnlyList<PreprocessConflictDetailDto>? ConflictDetails);

    public sealed record ExecuteTaskResponse(
        [property: JsonPropertyName("display_status")] string DisplayStatus,
        [property: JsonPropertyName("run_id")] Guid? RunId,
        [property: JsonPropertyName("schedule_id")] Guid? ScheduleId,
        [property: JsonPropertyName("job_id")] string? JobId,
        [property: JsonPropertyName("status")] string Status);

    public sealed record TaskProcessedDataColumnResponse(
        [property: JsonPropertyName("param_id")] string ParamId,
        [property: JsonPropertyName("label")] string Label);

    public sealed record TaskProcessedDataCellResponse(
        [property: JsonPropertyName("value")] double? Value,
        [property: JsonPropertyName("is_outlier")] bool IsOutlier,
        [property: JsonPropertyName("is_confirmed_outlier")] bool IsConfirmedOutlier,
        [property: JsonPropertyName("review_status")] string? ReviewStatus);

    public sealed record TaskProcessedDataRowResponse(
        [property: JsonPropertyName("ts")] string Ts,
        [property: JsonPropertyName("cells")] IReadOnlyDictionary<string, TaskProcessedDataCellResponse> Cells);

    public sealed record TaskProcessedDataResponse(
        [property: JsonPropertyName("run_id")] Guid RunId,
        [property: JsonPropertyName("columns")] IReadOnlyList<TaskProcessedDataColumnResponse> Columns,
        [property: JsonPropertyName("rows")] IReadOnlyList<TaskProcessedDataRowResponse> Rows,
        [property: JsonPropertyName("total")] long Total,
        [property: JsonPropertyName("page")] int Page,
        [property: JsonPropertyName("page_size")] int PageSize);

    public sealed record TaskOutlierPointItemResponse(
        [property: JsonPropertyName("review_id")] Guid ReviewId,
        [property: JsonPropertyName("param_id")] string ParamId,
        [property: JsonPropertyName("param_label")] string ParamLabel,
        [property: JsonPropertyName("ts")] string Ts,
        [property: JsonPropertyName("value")] double Value,
        [property: JsonPropertyName("outlier_method")] string OutlierMethod,
        [property: JsonPropertyName("review_status")] string ReviewStatus,
        [property: JsonPropertyName("remark")] string? Remark);

    public sealed record TaskOutlierPointsResponse(
        [property: JsonPropertyName("run_id")] Guid RunId,
        [property: JsonPropertyName("items")] IReadOnlyList<TaskOutlierPointItemResponse> Items,
        [property: JsonPropertyName("total")] long Total,
        [property: JsonPropertyName("page")] int Page,
        [property: JsonPropertyName("page_size")] int PageSize);

    public sealed record TaskOutlierSegmentItemResponse(
        [property: JsonPropertyName("param_id")] string ParamId,
        [property: JsonPropertyName("param_label")] string ParamLabel,
        [property: JsonPropertyName("segment_start")] string SegmentStart,
        [property: JsonPropertyName("segment_end")] string SegmentEnd,
        [property: JsonPropertyName("outlier_method")] string OutlierMethod,
        [property: JsonPropertyName("duration_seconds")] double DurationSeconds,
        [property: JsonPropertyName("segment_kind")] string SegmentKind);

    public sealed record TaskOutlierSegmentsResponse(
        [property: JsonPropertyName("run_id")] Guid RunId,
        [property: JsonPropertyName("items")] IReadOnlyList<TaskOutlierSegmentItemResponse> Items,
        [property: JsonPropertyName("total")] int Total,
        [property: JsonPropertyName("segment_kind")] string SegmentKind,
        [property: JsonPropertyName("review_completed")] bool ReviewCompleted);

    public sealed record TaskValidRangeItemResponse(
        [property: JsonPropertyName("range_start")] string RangeStart,
        [property: JsonPropertyName("range_end")] string RangeEnd,
        [property: JsonPropertyName("duration_seconds")] double DurationSeconds);

    public sealed record TaskValidRangesResponse(
        [property: JsonPropertyName("run_id")] Guid RunId,
        [property: JsonPropertyName("items")] IReadOnlyList<TaskValidRangeItemResponse> Items,
        [property: JsonPropertyName("total")] int Total);

    public sealed record OutlierReviewSummaryResponse(
        [property: JsonPropertyName("run_id")] Guid RunId,
        [property: JsonPropertyName("outlier_review_status")] string? OutlierReviewStatus,
        [property: JsonPropertyName("auto_count")] int AutoCount,
        [property: JsonPropertyName("pending_count")] int PendingCount,
        [property: JsonPropertyName("confirmed_count")] int ConfirmedCount,
        [property: JsonPropertyName("jitter_count")] int JitterCount,
        [property: JsonPropertyName("status_counts")] IReadOnlyDictionary<string, int> StatusCounts,
        [property: JsonPropertyName("mark_options")] IReadOnlyList<OutlierMarkOptionResponse> MarkOptions);

    public sealed record OutlierMarkOptionResponse(
        [property: JsonPropertyName("mark_code")] string MarkCode,
        [property: JsonPropertyName("mark_label")] string MarkLabel,
        [property: JsonPropertyName("is_outlier")] bool IsOutlier,
        [property: JsonPropertyName("sort_order")] int SortOrder,
        [property: JsonPropertyName("enabled")] bool Enabled);

    public sealed record OutlierReviewItemResponse(
        [property: JsonPropertyName("review_id")] Guid ReviewId,
        [property: JsonPropertyName("param_id")] string ParamId,
        [property: JsonPropertyName("param_label")] string ParamLabel,
        [property: JsonPropertyName("ts")] string Ts,
        [property: JsonPropertyName("value")] double? Value,
        [property: JsonPropertyName("outlier_method")] string OutlierMethod,
        [property: JsonPropertyName("review_status")] string ReviewStatus,
        [property: JsonPropertyName("remark")] string? Remark);

    public sealed record OutlierReviewListResponse(
        [property: JsonPropertyName("run_id")] Guid RunId,
        [property: JsonPropertyName("items")] IReadOnlyList<OutlierReviewItemResponse> Items,
        [property: JsonPropertyName("total")] long Total,
        [property: JsonPropertyName("page")] int Page,
        [property: JsonPropertyName("page_size")] int PageSize);

    public sealed record SubmitOutlierReviewItemBody(
        [property: JsonPropertyName("paramId")] string ParamId,
        [property: JsonPropertyName("ts")] string Ts,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("remark")] string? Remark);

    public sealed record SubmitOutlierReviewsBody(
        [property: JsonPropertyName("items")] IReadOnlyList<SubmitOutlierReviewItemBody> Items);

    public sealed record CompleteOutlierReviewResponse(
        [property: JsonPropertyName("run_id")] Guid RunId,
        [property: JsonPropertyName("outlier_review_status")] string OutlierReviewStatus,
        [property: JsonPropertyName("confirmed_segment_count")] int ConfirmedSegmentCount);

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

    public sealed record ExecuteRunBody(
        string? OnActiveConflict,
        string? OnCommittedConflict);

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
            new AcceptedJobResponse(result.JobId, result.RunId, null, result.Status.ToString()),
            HttpContext));
    }

    public sealed record CreatePreprocessBody(
        string TasookNo,
        string SatelliteNo,
        string? ExecutionMode,
        string? TestBatchName,
        DateTimeOffset? WindowStart,
        DateTimeOffset? WindowEnd,
        DateTimeOffset? ScheduledAt,
        string? DailyTime,
        int? IntervalDays,
        DateOnly? EffectiveFrom,
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

        if (!PreprocessCreateModeParser.TryParse(body.ExecutionMode, out var createMode))
        {
            return BadRequest(
                ApiResponse<object>.Fail(
                    TaskErrorCodes.ExecutionModeInvalid,
                    "executionMode 须为 IMMEDIATE、ONCE_SCHEDULED 或 DAILY_RECURRING",
                    HttpContext));
        }

        TimeOnly? dailyTime = null;
        if (!string.IsNullOrWhiteSpace(body.DailyTime))
        {
            if (!TimeOnly.TryParse(body.DailyTime, out var parsedTime))
            {
                return BadRequest(
                    ApiResponse<object>.Fail(
                        TaskErrorCodes.DailyTimeRequired,
                        "dailyTime 格式无效，应为 HH:mm:ss",
                        HttpContext));
            }

            dailyTime = parsedTime;
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
            TaskTriggerType.Api,
            createMode,
            body.ScheduledAt,
            dailyTime,
            body.IntervalDays,
            body.EffectiveFrom);

        try
        {
            var result = await orchestrator.CreatePreprocessAsync(cmd, createdBy: null, cancellationToken);
            return Ok(ApiResponse<AcceptedJobResponse>.Ok(
                new AcceptedJobResponse(result.JobId, result.RunId, result.ScheduleId, result.Status.ToString()),
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
                TaskErrorCodes.ScheduleTimeRequired => StatusCodes.Status422UnprocessableEntity,
                TaskErrorCodes.ScheduleTimeInvalid => StatusCodes.Status422UnprocessableEntity,
                TaskErrorCodes.DailyTimeRequired => StatusCodes.Status422UnprocessableEntity,
                TaskErrorCodes.EffectiveFromRequired => StatusCodes.Status422UnprocessableEntity,
                TaskErrorCodes.IntervalDaysInvalid => StatusCodes.Status422UnprocessableEntity,
                _ => StatusCodes.Status400BadRequest
            };
            return StatusCode(status, ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
    }

    [HttpPost("schedules/{scheduleId:guid}/disable")]
    public async Task<ActionResult<ApiResponse<object>>> DisableSchedule(
        Guid scheduleId,
        CancellationToken cancellationToken)
    {
        try
        {
            await scheduleService.DisableScheduleAsync(scheduleId, cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { scheduleId, enabled = false }, HttpContext));
        }
        catch (TaskValidationException ex) when (ex.ErrorCode == TaskErrorCodes.NotFound)
        {
            return NotFound(ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
    }

    /// <summary>任务列表（预处理 RUN + 每日 SCHEDULE 联合，默认 50 条，最大 200）。</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TaskListItemResponse>>>> List(
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var items = await taskListService.ListAsync(pageSize, cancellationToken).ConfigureAwait(false);
        return Ok(ApiResponse<IReadOnlyList<TaskListItemResponse>>.Ok(
            items.Select(ToListItem).ToArray(),
            HttpContext));
    }

    /// <summary>立即预处理任务手动执行（与 POST runs/{runId}/execute 相同）。</summary>
    [HttpPost("{runId:guid}/execute")]
    public Task<ActionResult<ApiResponse<ExecuteTaskResponse>>> ExecuteRunShortcut(
        Guid runId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ExecuteRunBody? body,
        CancellationToken cancellationToken) =>
        ExecuteRun(runId, body, cancellationToken);

    [HttpPost("runs/{runId:guid}/execute")]
    public async Task<ActionResult<ApiResponse<ExecuteTaskResponse>>> ExecuteRun(
        Guid runId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ExecuteRunBody? body,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await taskExecutionService
                .ExecuteRunAsync(runId, ParseConflictOptions(body), cancellationToken)
                .ConfigureAwait(false);
            return Ok(ApiResponse<ExecuteTaskResponse>.Ok(ToExecuteResponse(result), HttpContext));
        }
        catch (TaskValidationException ex) when (ex.ErrorCode == TaskErrorCodes.NotFound)
        {
            return NotFound(ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
        catch (TaskValidationException ex)
        {
            return StatusCode(
                StatusCodes.Status409Conflict,
                ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
    }

    [HttpDelete("{runId:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteRun(Guid runId, CancellationToken cancellationToken)
    {
        try
        {
            await taskLifecycleService.DeleteRunAsync(runId, cancellationToken).ConfigureAwait(false);
            return Ok(ApiResponse<object>.Ok(new { runId, deleted = true }, HttpContext));
        }
        catch (TaskValidationException ex) when (ex.ErrorCode == TaskErrorCodes.NotFound)
        {
            return NotFound(ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
        catch (TaskValidationException ex)
        {
            return StatusCode(
                StatusCodes.Status409Conflict,
                ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
    }

    [HttpPost("{runId:guid}/reexecute")]
    public async Task<ActionResult<ApiResponse<ExecuteTaskResponse>>> ReExecuteRun(
        Guid runId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ExecuteRunBody? body,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await taskLifecycleService
                .ReExecuteRunAsync(runId, ParseConflictOptions(body), cancellationToken)
                .ConfigureAwait(false);
            return Ok(ApiResponse<ExecuteTaskResponse>.Ok(ToExecuteResponse(result), HttpContext));
        }
        catch (TaskValidationException ex) when (ex.ErrorCode == TaskErrorCodes.NotFound)
        {
            return NotFound(ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
        catch (TaskValidationException ex)
        {
            return StatusCode(
                StatusCodes.Status409Conflict,
                ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
    }

    [HttpGet("{runId:guid}/processed-data")]
    public async Task<ActionResult<ApiResponse<TaskProcessedDataResponse>>> GetProcessedData(
        Guid runId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = TaskRunProcessedDataService.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var data = await taskProcessedDataService
                .GetProcessedDataAsync(runId, page, pageSize, cancellationToken)
                .ConfigureAwait(false);
            return Ok(ApiResponse<TaskProcessedDataResponse>.Ok(ToProcessedDataResponse(data), HttpContext));
        }
        catch (TaskValidationException ex) when (ex.ErrorCode == TaskErrorCodes.NotFound)
        {
            return NotFound(ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
        catch (TaskValidationException ex)
        {
            return StatusCode(
                StatusCodes.Status422UnprocessableEntity,
                ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
    }

    [HttpGet("{runId:guid}/outlier-reviews/summary")]
    public async Task<ActionResult<ApiResponse<OutlierReviewSummaryResponse>>> GetOutlierReviewSummary(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var data = await outlierReviewService.GetSummaryAsync(runId, cancellationToken).ConfigureAwait(false);
            return Ok(ApiResponse<OutlierReviewSummaryResponse>.Ok(ToOutlierReviewSummaryResponse(data), HttpContext));
        }
        catch (TaskValidationException ex) when (ex.ErrorCode == TaskErrorCodes.NotFound)
        {
            return NotFound(ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
        catch (TaskValidationException ex)
        {
            return StatusCode(
                StatusCodes.Status422UnprocessableEntity,
                ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
    }

    [HttpGet("{runId:guid}/outlier-reviews")]
    public async Task<ActionResult<ApiResponse<OutlierReviewListResponse>>> ListOutlierReviews(
        Guid runId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = TaskRunProcessedDataService.DefaultPageSize,
        [FromQuery] string? status = null,
        [FromQuery] string? paramId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var data = await outlierReviewService
                .ListReviewsAsync(runId, status, paramId, page, pageSize, cancellationToken)
                .ConfigureAwait(false);
            return Ok(ApiResponse<OutlierReviewListResponse>.Ok(ToOutlierReviewListResponse(data), HttpContext));
        }
        catch (TaskValidationException ex) when (ex.ErrorCode == TaskErrorCodes.NotFound)
        {
            return NotFound(ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
        catch (TaskValidationException ex)
        {
            return StatusCode(
                StatusCodes.Status422UnprocessableEntity,
                ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
    }

    [HttpPatch("{runId:guid}/outlier-reviews")]
    public async Task<ActionResult<ApiResponse<OutlierReviewSummaryResponse>>> SubmitOutlierReviews(
        Guid runId,
        [FromBody] SubmitOutlierReviewsBody body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var items = body.Items
                .Select(i => new SubmitOutlierReviewItemDto(i.ParamId, i.Ts, i.Status, i.Remark))
                .ToArray();
            var data = await outlierReviewService
                .SubmitReviewsAsync(runId, items, null, cancellationToken)
                .ConfigureAwait(false);
            return Ok(ApiResponse<OutlierReviewSummaryResponse>.Ok(ToOutlierReviewSummaryResponse(data), HttpContext));
        }
        catch (TaskValidationException ex) when (ex.ErrorCode == TaskErrorCodes.NotFound)
        {
            return NotFound(ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
        catch (TaskValidationException ex)
        {
            return StatusCode(
                StatusCodes.Status422UnprocessableEntity,
                ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
    }

    [HttpPost("{runId:guid}/outlier-reviews/complete")]
    public async Task<ActionResult<ApiResponse<CompleteOutlierReviewResponse>>> CompleteOutlierReview(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var data = await outlierReviewService.CompleteReviewAsync(runId, cancellationToken).ConfigureAwait(false);
            return Ok(ApiResponse<CompleteOutlierReviewResponse>.Ok(
                new CompleteOutlierReviewResponse(data.RunId, data.OutlierReviewStatus, data.ConfirmedSegmentCount),
                HttpContext));
        }
        catch (TaskValidationException ex) when (ex.ErrorCode == TaskErrorCodes.NotFound)
        {
            return NotFound(ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
        catch (TaskValidationException ex)
        {
            return StatusCode(
                StatusCodes.Status422UnprocessableEntity,
                ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
    }

    [HttpGet("{runId:guid}/outlier-points")]
    public async Task<ActionResult<ApiResponse<TaskOutlierPointsResponse>>> GetOutlierPoints(
        Guid runId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = TaskRunProcessedDataService.DefaultPageSize,
        [FromQuery] string? paramId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var data = await taskProcessedDataService
                .GetOutlierPointsAsync(runId, page, pageSize, paramId, status, cancellationToken)
                .ConfigureAwait(false);
            return Ok(ApiResponse<TaskOutlierPointsResponse>.Ok(ToOutlierPointsResponse(data), HttpContext));
        }
        catch (TaskValidationException ex) when (ex.ErrorCode == TaskErrorCodes.NotFound)
        {
            return NotFound(ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
        catch (TaskValidationException ex)
        {
            return StatusCode(
                StatusCodes.Status422UnprocessableEntity,
                ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
    }

    [HttpGet("{runId:guid}/outlier-segments")]
    public async Task<ActionResult<ApiResponse<TaskOutlierSegmentsResponse>>> GetOutlierSegments(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var data = await taskProcessedDataService
                .GetOutlierSegmentsAsync(runId, cancellationToken)
                .ConfigureAwait(false);
            return Ok(ApiResponse<TaskOutlierSegmentsResponse>.Ok(ToOutlierSegmentsResponse(data), HttpContext));
        }
        catch (TaskValidationException ex) when (ex.ErrorCode == TaskErrorCodes.NotFound)
        {
            return NotFound(ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
        catch (TaskValidationException ex)
        {
            return StatusCode(
                StatusCodes.Status422UnprocessableEntity,
                ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
    }

    [HttpGet("{runId:guid}/valid-ranges")]
    public async Task<ActionResult<ApiResponse<TaskValidRangesResponse>>> GetValidRanges(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var data = await taskProcessedDataService
                .GetValidRangesAsync(runId, cancellationToken)
                .ConfigureAwait(false);
            return Ok(ApiResponse<TaskValidRangesResponse>.Ok(ToValidRangesResponse(data), HttpContext));
        }
        catch (TaskValidationException ex) when (ex.ErrorCode == TaskErrorCodes.NotFound)
        {
            return NotFound(ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
        catch (TaskValidationException ex)
        {
            return StatusCode(
                StatusCodes.Status422UnprocessableEntity,
                ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
    }

    [HttpPost("schedules/{scheduleId:guid}/execute")]
    public async Task<ActionResult<ApiResponse<ExecuteTaskResponse>>> ExecuteSchedule(
        Guid scheduleId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await taskExecutionService.ExecuteScheduleAsync(scheduleId, cancellationToken)
                .ConfigureAwait(false);
            return Ok(ApiResponse<ExecuteTaskResponse>.Ok(ToExecuteResponse(result), HttpContext));
        }
        catch (TaskValidationException ex) when (ex.ErrorCode == TaskErrorCodes.NotFound)
        {
            return NotFound(ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, HttpContext));
        }
    }

    [HttpGet("runs/{runId:guid}/executions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TaskExecutionRecordResponse>>>> ListRunExecutions(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var records = await taskListService.ListExecutionsForRunAsync(runId, cancellationToken).ConfigureAwait(false);
        if (records.Count == 0)
        {
            var run = await taskRuns.GetByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
            if (run is null)
            {
                return NotFound(ApiResponse<object>.Fail(TaskErrorCodes.NotFound, "任务不存在", HttpContext));
            }
        }

        return Ok(ApiResponse<IReadOnlyList<TaskExecutionRecordResponse>>.Ok(
            records.Select(ToExecutionRecord).ToArray(),
            HttpContext));
    }

    [HttpGet("schedules/{scheduleId:guid}/executions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TaskExecutionRecordResponse>>>> ListScheduleExecutions(
        Guid scheduleId,
        CancellationToken cancellationToken)
    {
        var schedule = await scheduleRepository.GetByIdAsync(scheduleId, cancellationToken).ConfigureAwait(false);
        if (schedule is null)
        {
            return NotFound(ApiResponse<object>.Fail(TaskErrorCodes.NotFound, "定时计划不存在", HttpContext));
        }

        var records = await taskListService.ListExecutionsForScheduleAsync(scheduleId, cancellationToken)
            .ConfigureAwait(false);
        return Ok(ApiResponse<IReadOnlyList<TaskExecutionRecordResponse>>.Ok(
            records.Select(ToExecutionRecord).ToArray(),
            HttpContext));
    }

    [HttpGet("{runId:guid}")]
    public async Task<ActionResult<ApiResponse<TaskRunDetailResponse>>> GetRun(Guid runId, CancellationToken cancellationToken)
    {
        var run = await taskRuns.GetByRunIdAsync(runId, cancellationToken);
        if (run is null)
        {
            return NotFound(ApiResponse<object>.Fail(TaskErrorCodes.NotFound, "任务不存在", HttpContext));
        }

        var detail = await ToDetailAsync(run, cancellationToken).ConfigureAwait(false);
        return Ok(ApiResponse<TaskRunDetailResponse>.Ok(detail, HttpContext));
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
                new AcceptedJobResponse(result.JobId, result.RunId, null, result.Status.ToString()),
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

    private async Task<TaskRunDetailResponse> ToDetailAsync(TaskRun r, CancellationToken cancellationToken)
    {
        string? filterTemplateName = null;
        if (r.FilterTemplateId is Guid filterId && r.FilterTemplateVersion is int filterVersion)
        {
            var filter = await filterTemplates.GetVersionAsync(filterId, filterVersion, cancellationToken)
                .ConfigureAwait(false);
            filterTemplateName = filter?.TemplateName;
        }

        string? algorithmTemplateName = null;
        if (r.AlgorithmTemplateId is Guid algoId && r.AlgorithmTemplateVersion is int algoVersion)
        {
            var algo = await algorithmTemplates.GetVersionAsync(algoId, algoVersion, cancellationToken)
                .ConfigureAwait(false);
            algorithmTemplateName = algo?.TemplateName;
        }

        PreprocessSchedule? schedule = null;
        if (r.ScheduleId is Guid sid)
        {
            schedule = await scheduleRepository.GetByIdAsync(sid, cancellationToken).ConfigureAwait(false);
        }

        return new TaskRunDetailResponse(
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
            filterTemplateName,
            r.AlgorithmTemplateId,
            r.AlgorithmTemplateVersion,
            algorithmTemplateName,
            r.ProgressPercent,
            r.CurrentStep,
            r.StartTime,
            r.EndTime,
            r.CreatedAt,
            r.ErrorCode,
            r.ErrorMsg,
            ExecutionModeToApi(r.ExecutionMode),
            r.ScheduledAt,
            r.ScheduleId,
            schedule?.DailyTime.ToString("HH:mm:ss"),
            schedule?.IntervalDays,
            schedule?.EffectiveFrom,
            await conflictReader.TryGetConflictDetailsAsync(r, cancellationToken).ConfigureAwait(false));
    }

    private static string? ExecutionModeToApi(PreprocessExecutionMode? mode) =>
        mode switch
        {
            PreprocessExecutionMode.OnceScheduled => "ONCE_SCHEDULED",
            PreprocessExecutionMode.DailyInstance => "DAILY_INSTANCE",
            PreprocessExecutionMode.Immediate => "IMMEDIATE",
            _ => null
        };

    private static TaskListItemResponse ToListItem(TaskListItemDto i) =>
        new(
            i.ItemType,
            i.ItemId,
            i.RunId,
            i.ScheduleId,
            i.JobId,
            i.JobType,
            i.ExecutionMode,
            i.CanExecute,
            i.CanDelete,
            i.CanReExecute,
            i.CanViewData,
            i.OutlierPendingCount,
            i.OutlierReviewStatus,
            i.StatusSummary,
            i.DisplayStatus,
            i.Status,
            i.TasookNo,
            i.SatelliteNo,
            i.TestBatchName,
            i.ProgressPercent,
            i.CurrentStep,
            i.ScheduledAt,
            i.CreatedAt,
            i.EndTime,
            i.ErrorCode,
            i.ErrorMsg);

    private static TaskExecutionRecordResponse ToExecutionRecord(TaskExecutionRecordDto r) =>
        new(
            r.RunId,
            r.JobId,
            r.Status,
            r.DisplayStatus,
            r.StartedAt,
            r.EndedAt,
            r.WindowStart,
            r.WindowEnd,
            r.ErrorCode,
            r.ErrorMsg,
            r.ConflictDetails);

    private static ExecuteTaskResponse ToExecuteResponse(ExecuteTaskResultDto r) =>
        new(r.DisplayStatus, r.RunId, r.ScheduleId, r.JobId, r.Status);

    private static TaskProcessedDataResponse ToProcessedDataResponse(TaskProcessedDataDto d) =>
        new(
            d.RunId,
            d.Columns.Select(c => new TaskProcessedDataColumnResponse(c.ParamId, c.Label)).ToArray(),
            d.Rows.Select(r => new TaskProcessedDataRowResponse(
                r.Ts,
                r.Cells.ToDictionary(
                    kv => kv.Key,
                    kv => new TaskProcessedDataCellResponse(
                        kv.Value.Value,
                        kv.Value.IsOutlier,
                        kv.Value.IsConfirmedOutlier,
                        kv.Value.ReviewStatus),
                    StringComparer.Ordinal))).ToArray(),
            d.Total,
            d.Page,
            d.PageSize);

    private static TaskOutlierPointsResponse ToOutlierPointsResponse(TaskOutlierPointsDto d) =>
        new(
            d.RunId,
            d.Items.Select(i => new TaskOutlierPointItemResponse(
                i.ReviewId,
                i.ParamId,
                i.ParamLabel,
                i.Ts,
                i.Value,
                i.OutlierMethod,
                i.ReviewStatus,
                i.Remark)).ToArray(),
            d.Total,
            d.Page,
            d.PageSize);

    private static TaskOutlierSegmentsResponse ToOutlierSegmentsResponse(TaskOutlierSegmentsDto d) =>
        new(
            d.RunId,
            d.Items.Select(i => new TaskOutlierSegmentItemResponse(
                i.ParamId,
                i.ParamLabel,
                i.SegmentStart,
                i.SegmentEnd,
                i.OutlierMethod,
                i.DurationSeconds,
                i.SegmentKind)).ToArray(),
            d.Total,
            d.SegmentKind,
            d.ReviewCompleted);

    private static OutlierReviewSummaryResponse ToOutlierReviewSummaryResponse(OutlierReviewSummaryDto d) =>
        new(
            d.RunId,
            d.OutlierReviewStatus,
            d.AutoCount,
            d.PendingCount,
            d.ConfirmedCount,
            d.JitterCount,
            d.StatusCounts,
            d.MarkOptions
                .Select(x => new OutlierMarkOptionResponse(x.MarkCode, x.MarkLabel, x.IsOutlier, x.SortOrder, x.Enabled))
                .ToArray());

    private static OutlierReviewListResponse ToOutlierReviewListResponse(OutlierReviewListDto d) =>
        new(
            d.RunId,
            d.Items.Select(i => new OutlierReviewItemResponse(
                i.ReviewId,
                i.ParamId,
                i.ParamLabel,
                i.Ts,
                i.Value,
                i.OutlierMethod,
                i.ReviewStatus,
                i.Remark)).ToArray(),
            d.Total,
            d.Page,
            d.PageSize);

    private static TaskValidRangesResponse ToValidRangesResponse(TaskValidRangesDto d) =>
        new(
            d.RunId,
            d.Items.Select(x => new TaskValidRangeItemResponse(x.RangeStart, x.RangeEnd, x.DurationSeconds)).ToArray(),
            d.Total);

    private static PreprocessConflictHandlingOptions? ParseConflictOptions(ExecuteRunBody? body)
    {
        if (body is null)
        {
            return null;
        }

        var hasAny = !string.IsNullOrWhiteSpace(body.OnActiveConflict)
                     || !string.IsNullOrWhiteSpace(body.OnCommittedConflict);
        if (!hasAny)
        {
            return null;
        }

        var active = string.Equals(body.OnActiveConflict, "SKIP", StringComparison.OrdinalIgnoreCase)
            ? ActiveConflictHandling.Skip
            : ActiveConflictHandling.Fail;
        var committed = body.OnCommittedConflict?.Trim().ToUpperInvariant() switch
        {
            "SKIP" => CommittedConflictHandling.Skip,
            "OVERWRITE" => CommittedConflictHandling.Overwrite,
            _ => CommittedConflictHandling.Fail
        };

        return new PreprocessConflictHandlingOptions(active, committed);
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
