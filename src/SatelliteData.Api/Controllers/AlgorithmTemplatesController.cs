using Microsoft.AspNetCore.Mvc;
using SatelliteData.Application.Assets;
using SatelliteData.Application.Templates;
using SatelliteData.Domain.Templates;

namespace SatelliteData.Api.Controllers;

[ApiController]
[Route("api/v1/templates/algorithms")]
public sealed class AlgorithmTemplatesController(AlgorithmTemplateService templateService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AlgorithmTemplateView>>>> List(
        [FromQuery] TemplateStatus? status,
        [FromQuery] string? keyword,
        [FromQuery] int pageNo = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var page = await templateService.ListAsync(
            new AlgorithmTemplateListRequest(status, keyword, pageNo, pageSize),
            cancellationToken);
        return Ok(ApiResponse<PagedResult<AlgorithmTemplateView>>.Ok(page, HttpContext));
    }

    [HttpGet("{templateId:guid}/versions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<AlgorithmTemplateView>>>> ListVersions(
        Guid templateId,
        CancellationToken cancellationToken)
    {
        try
        {
            var versions = await templateService.GetVersionsAsync(templateId, cancellationToken);
            return Ok(ApiResponse<IReadOnlyCollection<AlgorithmTemplateView>>.Ok(versions, HttpContext));
        }
        catch (TemplateGovernanceException ex)
        {
            return MapError<IReadOnlyCollection<AlgorithmTemplateView>>(ex);
        }
    }

    [HttpGet("{templateId:guid}/versions/{version:int}")]
    public async Task<ActionResult<ApiResponse<AlgorithmTemplateDetail>>> GetVersion(
        Guid templateId,
        int version,
        CancellationToken cancellationToken)
    {
        try
        {
            var detail = await templateService.GetVersionDetailAsync(templateId, version, cancellationToken);
            return Ok(ApiResponse<AlgorithmTemplateDetail>.Ok(detail, HttpContext));
        }
        catch (TemplateGovernanceException ex)
        {
            return MapError<AlgorithmTemplateDetail>(ex);
        }
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<AlgorithmTemplateDetail>>> Create(
        [FromBody] CreateAlgorithmTemplateRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var detail = await templateService.CreateAsync(request, GetOperatorId(), cancellationToken);
            return Ok(ApiResponse<AlgorithmTemplateDetail>.Ok(detail, HttpContext));
        }
        catch (TemplateGovernanceException ex)
        {
            return MapError<AlgorithmTemplateDetail>(ex);
        }
    }

    [HttpPut("{templateId:guid}/versions/{version:int}")]
    public async Task<ActionResult<ApiResponse<AlgorithmTemplateDetail>>> Update(
        Guid templateId,
        int version,
        [FromBody] UpdateAlgorithmTemplateRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var detail = await templateService.UpdateAsync(templateId, version, request, GetOperatorId(), cancellationToken);
            return Ok(ApiResponse<AlgorithmTemplateDetail>.Ok(detail, HttpContext));
        }
        catch (TemplateGovernanceException ex)
        {
            return MapError<AlgorithmTemplateDetail>(ex);
        }
    }

    [HttpPost("{templateId:guid}/versions/{version:int}/validate")]
    public async Task<ActionResult<ApiResponse<AlgorithmTemplateValidationResult>>> Validate(
        Guid templateId,
        int version,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await templateService.ValidateAsync(templateId, version, cancellationToken);
            return Ok(ApiResponse<AlgorithmTemplateValidationResult>.Ok(result, HttpContext));
        }
        catch (TemplateGovernanceException ex)
        {
            return MapError<AlgorithmTemplateValidationResult>(ex);
        }
    }

    [HttpPost("{templateId:guid}/versions/{version:int}/publish")]
    public async Task<ActionResult<ApiResponse<AlgorithmTemplateView>>> Publish(
        Guid templateId,
        int version,
        CancellationToken cancellationToken)
    {
        try
        {
            var view = await templateService.PublishAsync(templateId, version, GetOperatorId(), cancellationToken);
            return Ok(ApiResponse<AlgorithmTemplateView>.Ok(view, HttpContext));
        }
        catch (TemplateGovernanceException ex)
        {
            return MapError<AlgorithmTemplateView>(ex);
        }
    }

    [HttpPost("{templateId:guid}/versions/{version:int}/archive")]
    public async Task<ActionResult<ApiResponse<AlgorithmTemplateView>>> Archive(
        Guid templateId,
        int version,
        CancellationToken cancellationToken)
    {
        try
        {
            var view = await templateService.ArchiveAsync(templateId, version, GetOperatorId(), cancellationToken);
            return Ok(ApiResponse<AlgorithmTemplateView>.Ok(view, HttpContext));
        }
        catch (TemplateGovernanceException ex)
        {
            return MapError<AlgorithmTemplateView>(ex);
        }
    }

    [HttpPost("{templateId:guid}/clone")]
    public async Task<ActionResult<ApiResponse<AlgorithmTemplateDetail>>> Clone(
        Guid templateId,
        [FromQuery] int? sourceVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            var detail = await templateService.CloneAsync(templateId, sourceVersion, GetOperatorId(), cancellationToken);
            return Ok(ApiResponse<AlgorithmTemplateDetail>.Ok(detail, HttpContext));
        }
        catch (TemplateGovernanceException ex)
        {
            return MapError<AlgorithmTemplateDetail>(ex);
        }
    }

    [HttpGet("{templateId:guid}/delete-impact")]
    public async Task<ActionResult<ApiResponse<AlgorithmTemplateDeleteImpact>>> GetDeleteImpact(
        Guid templateId,
        CancellationToken cancellationToken)
    {
        try
        {
            var impact = await templateService.GetDeleteImpactAsync(templateId, cancellationToken);
            return Ok(ApiResponse<AlgorithmTemplateDeleteImpact>.Ok(impact, HttpContext));
        }
        catch (TemplateGovernanceException ex)
        {
            return MapError<AlgorithmTemplateDeleteImpact>(ex);
        }
    }

    [HttpDelete("{templateId:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteTemplate(
        Guid templateId,
        [FromQuery] bool cascade = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await templateService.DeleteTemplateAsync(templateId, cascade, cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { deleted = true }, HttpContext));
        }
        catch (TemplateGovernanceException ex)
        {
            return MapError<object>(ex);
        }
    }

    [HttpDelete("{templateId:guid}/versions/{version:int}")]
    public async Task<ActionResult<ApiResponse<object>>> Delete(
        Guid templateId,
        int version,
        CancellationToken cancellationToken)
    {
        try
        {
            await templateService.DeleteAsync(templateId, version, cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { deleted = true }, HttpContext));
        }
        catch (TemplateGovernanceException ex)
        {
            return MapError<object>(ex);
        }
    }

    [HttpPost("{templateId:guid}/versions/{version:int}/trial-run")]
    public async Task<ActionResult<ApiResponse<AlgorithmTemplateTrialRunResponse>>> TrialRun(
        Guid templateId,
        int version,
        [FromBody] AlgorithmTemplateTrialRunRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var resp = await templateService.TrialRunAsync(templateId, version, request, cancellationToken);
            return Ok(ApiResponse<AlgorithmTemplateTrialRunResponse>.Ok(resp, HttpContext));
        }
        catch (TemplateGovernanceException ex)
        {
            return MapError<AlgorithmTemplateTrialRunResponse>(ex);
        }
    }

    private Guid? GetOperatorId()
    {
        var userIdClaim = User?.FindFirst("sub")?.Value;
        return Guid.TryParse(userIdClaim, out var id) ? id : null;
    }

    private ActionResult<ApiResponse<T>> MapError<T>(TemplateGovernanceException ex)
    {
        var status = ex.ErrorCode switch
        {
            TemplateErrorCodes.AlgorithmTemplateNotFound => StatusCodes.Status404NotFound,
            TemplateErrorCodes.AlgorithmTemplateNotEditable => StatusCodes.Status409Conflict,
            TemplateErrorCodes.AlgorithmTemplateInvalidState => StatusCodes.Status409Conflict,
            TemplateErrorCodes.AlgorithmTemplateDagInvalid => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status400BadRequest
        };
        return StatusCode(status, ApiResponse<T>.Fail(ex.ErrorCode, ex.Message, HttpContext));
    }
}
