using Microsoft.AspNetCore.Mvc;
using SatelliteData.Application.Templates;
using SatelliteData.Domain.Templates;

namespace SatelliteData.Api.Controllers;

[ApiController]
[Route("api/v1/algorithms")]
public sealed class AlgorithmRegistryController(AlgorithmRegistryService registryService) : ControllerBase
{
    [HttpGet("registry")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<AlgorithmRegistryEntry>>>> GetRegistry(
        CancellationToken cancellationToken)
    {
        var entries = await registryService.GetPublishedRegistryAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyCollection<AlgorithmRegistryEntry>>.Ok(entries, HttpContext));
    }

    [HttpGet("packages")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<AlgorithmPackageView>>>> List(
        [FromQuery] AlgorithmPackageStatus? status,
        [FromQuery] AlgorithmRuntime? runtime,
        [FromQuery] AlgorithmCategory? category,
        CancellationToken cancellationToken = default)
    {
        var items = await registryService.ListAsync(status, runtime, category, cancellationToken);
        return Ok(ApiResponse<IReadOnlyCollection<AlgorithmPackageView>>.Ok(items, HttpContext));
    }

    [HttpGet("packages/{packageId:guid}")]
    public async Task<ActionResult<ApiResponse<AlgorithmPackageDetail>>> Get(
        Guid packageId,
        CancellationToken cancellationToken)
    {
        var detail = await registryService.GetDetailAsync(packageId, cancellationToken);
        if (detail is null)
        {
            return NotFound(ApiResponse<object>.Fail(TemplateErrorCodes.AlgorithmPackageNotFound, "算法包不存在", HttpContext));
        }
        return Ok(ApiResponse<AlgorithmPackageDetail>.Ok(detail, HttpContext));
    }
}
