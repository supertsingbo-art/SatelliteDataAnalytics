using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using SatelliteData.Application.Tasks;

namespace SatelliteData.Api.Controllers;

[ApiController]
[Route("api/v1/algorithm-results")]
public sealed class AlgorithmResultsController(AlgorithmResultQueryService queryService) : ControllerBase
{
    public sealed record AlgorithmResultItemResponse(
        [property: JsonPropertyName("node_id")] string NodeId,
        [property: JsonPropertyName("algorithm_code")] string AlgorithmCode,
        [property: JsonPropertyName("metric_name")] string MetricName,
        [property: JsonPropertyName("metric_value")] double MetricValue,
        [property: JsonPropertyName("detail_json")] string DetailJson,
        [property: JsonPropertyName("window_start")] DateTimeOffset WindowStart,
        [property: JsonPropertyName("window_end")] DateTimeOffset WindowEnd,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

    public sealed record AlgorithmResultListResponse(
        [property: JsonPropertyName("run_id")] Guid RunId,
        [property: JsonPropertyName("total")] int Total,
        [property: JsonPropertyName("items")] IReadOnlyList<AlgorithmResultItemResponse> Items);

    [HttpGet("{runId:guid}")]
    public async Task<ActionResult<ApiResponse<AlgorithmResultListResponse>>> GetByRunId(
        Guid runId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await queryService.GetByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
            return Ok(ApiResponse<AlgorithmResultListResponse>.Ok(
                new AlgorithmResultListResponse(
                    result.RunId,
                    result.Total,
                    result.Items.Select(i => new AlgorithmResultItemResponse(
                        i.NodeId,
                        i.AlgorithmCode,
                        i.MetricName,
                        i.MetricValue,
                        i.DetailJson,
                        i.WindowStart,
                        i.WindowEnd,
                        i.CreatedAt)).ToArray()),
                HttpContext));
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
}
