using Microsoft.AspNetCore.Mvc;
using SatelliteData.Application.Assets;
using SatelliteData.Domain.Assets;

namespace SatelliteData.Api.Controllers;

[ApiController]
[Route("api/v1/asset/sources")]
public sealed class AssetSourcesController(
    DataSourceConfigService service,
    IHttpClientFactory httpClientFactory) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<DataSourceConfig>>>> GetSources(
        CancellationToken cancellationToken)
    {
        var configs = await service.GetConfigsAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyCollection<DataSourceConfig>>.Ok(configs, HttpContext));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<DataSourceConfig>>> CreateSource(
        [FromBody] SaveDataSourceConfigRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var config = await service.SaveConfigAsync(null, request, cancellationToken);
            return Ok(ApiResponse<DataSourceConfig>.Ok(config, HttpContext));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("ASSET_CONFIG_INVALID", ex.Message, HttpContext));
        }
    }

    [HttpPut("{sourceId:guid}")]
    public async Task<ActionResult<ApiResponse<DataSourceConfig>>> UpdateSource(
        Guid sourceId,
        [FromBody] SaveDataSourceConfigRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var config = await service.SaveConfigAsync(sourceId, request, cancellationToken);
            return Ok(ApiResponse<DataSourceConfig>.Ok(config, HttpContext));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("ASSET_CONFIG_INVALID", ex.Message, HttpContext));
        }
    }

    [HttpPatch("{sourceId:guid}/status")]
    public async Task<ActionResult<ApiResponse<DataSourceConfig>>> SetStatus(
        Guid sourceId,
        [FromBody] SetDataSourceStatusRequest request,
        CancellationToken cancellationToken)
    {
        var config = await service.SetStatusAsync(sourceId, request.Enabled, cancellationToken);
        if (config is null)
        {
            return NotFound(ApiResponse<object>.Fail("ASSET_SOURCE_NOT_FOUND", "data source config not found", HttpContext));
        }

        return Ok(ApiResponse<DataSourceConfig>.Ok(config, HttpContext));
    }

    [HttpPost("{sourceId:guid}/test")]
    public async Task<ActionResult<ApiResponse<ConnectionTestResult>>> TestConnection(
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        var result = await service.TestConnectionAsync(
            sourceId,
            async (config, token) =>
            {
                if (config.SourceType is DataSourceTypes.MassDataApi or DataSourceTypes.SatelliteAssetApi)
                {
                    var client = httpClientFactory.CreateClient();
                    client.Timeout = TimeSpan.FromMilliseconds(config.TimeoutMs);

                    ////fqb temp test
                    //if (config.EndpointUrl.Contains("5005"))
                    //{ 
                    //    using var response0 = await client.GetAsync($"{config.EndpointUrl}/swagger/index.html", token);
                    //    response0.EnsureSuccessStatusCode();
                    //    return;
                    //}  
                    using var response = await client.GetAsync(config.EndpointUrl, token);
                    response.EnsureSuccessStatusCode();
                }
            },
            cancellationToken);

        return Ok(ApiResponse<ConnectionTestResult>.Ok(result, HttpContext));
    }
}
