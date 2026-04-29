using Microsoft.AspNetCore.Mvc;
using SatelliteData.Application.Assets;
using SatelliteData.Domain.Assets;

namespace SatelliteData.Api.Controllers;

[ApiController]
[Route("api/v1/asset/satellites")]
public sealed class AssetSatellitesController(
    IAssetCacheRepository repository,
    MongoConnectionPool mongoConnectionPool) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<SatelliteCache>>>> GetSatellites(
        CancellationToken cancellationToken)
    {
        var satellites = await repository.GetSatellitesAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyCollection<SatelliteCache>>.Ok(satellites, HttpContext));
    }

    [HttpGet("{tasookNo}/{satelliteNo}")]
    public async Task<ActionResult<ApiResponse<SatelliteCache>>> GetSatellite(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken)
    {
        var satellite = await repository.GetSatelliteAsync(tasookNo, satelliteNo, cancellationToken);
        if (satellite is null)
        {
            return NotFound(ApiResponse<object>.Fail("ASSET_SATELLITE_NOT_FOUND", "satellite cache not found", HttpContext));
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
        pageNo = pageNo <= 0 ? 1 : pageNo;
        pageSize = pageSize <= 0 ? 50 : Math.Min(pageSize, 500);

        var parameters = await repository.GetParametersAsync(tasookNo, satelliteNo, cancellationToken);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            parameters = parameters
                .Where(item => item.ParamId.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || item.ParamName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        var page = new PagedResult<ParamCache>(
            pageNo,
            pageSize,
            parameters.Count,
            parameters.Skip((pageNo - 1) * pageSize).Take(pageSize).ToArray());

        return Ok(ApiResponse<PagedResult<ParamCache>>.Ok(page, HttpContext));
    }

    [HttpGet("{tasookNo}/{satelliteNo}/test-batches")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<TestBatchCache>>>> GetTestBatches(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken)
    {
        var testBatches = await repository.GetTestBatchesAsync(tasookNo, satelliteNo, cancellationToken);
        return Ok(ApiResponse<IReadOnlyCollection<TestBatchCache>>.Ok(testBatches, HttpContext));
    }

    [HttpGet("{tasookNo}/{satelliteNo}/mongo-info")]
    public async Task<ActionResult<ApiResponse<object>>> GetMongoInfo(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken)
    {
        try
        {
            var mongoInfo = await mongoConnectionPool.GetConnectionInfoAsync(tasookNo, satelliteNo, cancellationToken);
            var summary = new
            {
                mongoInfo.DbName,
                mongoInfo.AuthRef,
                MongoUri = MaskMongoUri(mongoInfo.MongoUri)
            };

            return Ok(ApiResponse<object>.Ok(summary, HttpContext));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ApiResponse<object>.Fail("ASSET_MONGO_NOT_FOUND", ex.Message, HttpContext));
        }
    }

    private static string MaskMongoUri(string uri)
    {
        var at = uri.IndexOf('@', StringComparison.Ordinal);
        var scheme = uri.IndexOf("://", StringComparison.Ordinal);
        if (at > 0 && scheme > 0 && at > scheme)
        {
            return uri[..(scheme + 3)] + "***:***" + uri[at..];
        }

        return uri;
    }
}
