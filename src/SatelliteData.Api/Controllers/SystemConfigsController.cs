using Microsoft.AspNetCore.Mvc;
using SatelliteData.Application.Tasks;
using SatelliteData.Domain.Tasks;
using System.Text.Json.Serialization;

namespace SatelliteData.Api.Controllers;

[ApiController]
[Route("api/v1/system-configs")]
public sealed class SystemConfigsController(OutlierMarkConfigService outlierMarkConfigService) : ControllerBase
{
    public sealed record OutlierMarkItemBody(
        [property: JsonPropertyName("mark_code")] string MarkCode,
        [property: JsonPropertyName("mark_label")] string MarkLabel,
        [property: JsonPropertyName("is_outlier")] bool IsOutlier,
        [property: JsonPropertyName("sort_order")] int SortOrder,
        [property: JsonPropertyName("enabled")] bool Enabled);

    public sealed record SaveOutlierMarksBody([property: JsonPropertyName("items")] IReadOnlyList<OutlierMarkItemBody> Items);

    [HttpGet("outlier-marks")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<OutlierMarkItemBody>>>> GetOutlierMarks(
        CancellationToken cancellationToken = default)
    {
        var options = await outlierMarkConfigService.ListAsync(cancellationToken).ConfigureAwait(false);
        var data = options
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.MarkCode, StringComparer.Ordinal)
            .Select(x => new OutlierMarkItemBody(x.MarkCode, x.MarkLabel, x.IsOutlier, x.SortOrder, x.Enabled))
            .ToArray();
        return Ok(ApiResponse<IReadOnlyList<OutlierMarkItemBody>>.Ok(data, HttpContext));
    }

    [HttpPut("outlier-marks")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<OutlierMarkItemBody>>>> SaveOutlierMarks(
        [FromBody] SaveOutlierMarksBody body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var saved = await outlierMarkConfigService
                .SaveAsync(
                    body.Items
                        .Select(x => new OutlierMarkOption(x.MarkCode, x.MarkLabel, x.IsOutlier, x.SortOrder, x.Enabled))
                        .ToArray(),
                    cancellationToken)
                .ConfigureAwait(false);
            var data = saved
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.MarkCode, StringComparer.Ordinal)
                .Select(x => new OutlierMarkItemBody(x.MarkCode, x.MarkLabel, x.IsOutlier, x.SortOrder, x.Enabled))
                .ToArray();
            return Ok(ApiResponse<IReadOnlyList<OutlierMarkItemBody>>.Ok(data, HttpContext));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("OUTLIER_MARK_CONFIG_INVALID", ex.Message, HttpContext));
        }
    }
}
