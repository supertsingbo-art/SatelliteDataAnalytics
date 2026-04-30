using Microsoft.AspNetCore.Mvc;
using SatelliteData.Application.Assets;
using SatelliteData.Domain.Assets;

namespace SatelliteData.Api.Controllers;

[ApiController]
[Route("api/v1/asset")]
public sealed class AssetSyncController(AssetSyncService syncService) : ControllerBase
{
    [HttpPost("sync")]
    public async Task<ActionResult<ApiResponse<AssetSyncResult>>> SyncAll(
        CancellationToken cancellationToken)
    {
        var result = await syncService.SyncAllAsync(cancellationToken);
        return Ok(ApiResponse<AssetSyncResult>.Ok(result, HttpContext));
    }

    [HttpPost("satellites/{tasookNo}/{satelliteNo}/sync")]
    public async Task<ActionResult<ApiResponse<AssetSyncResult>>> SyncSatellite(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken)
    {
        var result = await syncService.SyncSatelliteAsync(tasookNo, satelliteNo, cancellationToken);
        return Ok(ApiResponse<AssetSyncResult>.Ok(result, HttpContext));
    }

    [HttpDelete("cache")]
    public async Task<ActionResult<ApiResponse<object>>> ClearCache(CancellationToken cancellationToken)
    {
        await syncService.ClearAllCacheAsync(cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { cleared = true }, HttpContext));
    }
}
