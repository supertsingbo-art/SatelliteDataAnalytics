using Microsoft.AspNetCore.Mvc;
using SatelliteData.Application.Assets;
using SatelliteData.Domain.Assets;

namespace SatelliteData.Api.Controllers;

[ApiController]
[Route("api/v1/asset/satellites")]
public sealed class AssetSatellitesController(AssetQueryService queryService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<SatelliteListItem>>>> GetSatellites(
        [FromQuery] string? keyword,
        [FromQuery] int pageNo = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var page = await queryService.GetSatellitesAsync(
            new AssetPageRequest(keyword, pageNo, pageSize),
            cancellationToken);
        return Ok(ApiResponse<PagedResult<SatelliteListItem>>.Ok(page, HttpContext));
    }

    [HttpGet("{tasookNo}/{satelliteNo}")]
    public async Task<ActionResult<ApiResponse<SatelliteCache>>> GetSatellite(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken)
    {
        var satellite = await queryService.GetSatelliteAsync(tasookNo, satelliteNo, cancellationToken);
        if (satellite is null)
        {
            return NotFound(ApiResponse<object>.Fail(
                "ASSET_SATELLITE_NOT_FOUND",
                "卫星缓存不存在",
                HttpContext));
        }

        return Ok(ApiResponse<SatelliteCache>.Ok(satellite, HttpContext));
    }

    [HttpGet("{tasookNo}/{satelliteNo}/params")]
    public async Task<ActionResult<ApiResponse<PagedResult<ParamCache>>>> GetParameters(
        string tasookNo,
        string satelliteNo,
        [FromQuery] string? keyword,
        [FromQuery] int pageNo = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var page = await queryService.GetParametersAsync(
            tasookNo,
            satelliteNo,
            new AssetPageRequest(keyword, pageNo, pageSize),
            cancellationToken);

        return Ok(ApiResponse<PagedResult<ParamCache>>.Ok(page, HttpContext));
    }

    [HttpGet("{tasookNo}/{satelliteNo}/test-phases")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<TestBatchCache>>>> GetTestPhases(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken)
    {
        var phases = await queryService.GetTestPhasesAsync(tasookNo, satelliteNo, cancellationToken);
        return Ok(ApiResponse<IReadOnlyCollection<TestBatchCache>>.Ok(phases, HttpContext));
    }

    [HttpGet("{tasookNo}/{satelliteNo}/mongo-info")]
    public async Task<ActionResult<ApiResponse<MongoConnectionSummary>>> GetMongoInfo(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken)
    {
        try
        {
            var summary = await queryService.GetMongoInfoSummaryAsync(tasookNo, satelliteNo, cancellationToken);
            return Ok(ApiResponse<MongoConnectionSummary>.Ok(summary, HttpContext));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ApiResponse<object>.Fail("ASSET_MONGO_NOT_FOUND", ex.Message, HttpContext));
        }
    }
}
