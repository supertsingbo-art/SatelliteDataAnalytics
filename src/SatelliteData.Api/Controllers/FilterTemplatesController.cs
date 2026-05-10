using Microsoft.AspNetCore.Mvc;
using SatelliteData.Application.Assets;
using SatelliteData.Application.Templates;
using SatelliteData.Domain.Templates;

namespace SatelliteData.Api.Controllers;

[ApiController]
[Route("api/v1/templates/filters")]
public sealed class FilterTemplatesController(FilterTemplateService templateService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<FilterTemplateView>>>> List(
        [FromQuery] Guid? groupId,
        [FromQuery] TemplateStatus? status,
        [FromQuery] string? keyword,
        [FromQuery] int pageNo = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var page = await templateService.ListAsync(
            new FilterTemplateListRequest(groupId, status, keyword, pageNo, pageSize),
            cancellationToken);
        return Ok(ApiResponse<PagedResult<FilterTemplateView>>.Ok(page, HttpContext));
    }

    [HttpGet("applicable")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<FilterTemplateView>>>> Applicable(
        [FromQuery] string taskNo,
        [FromQuery] string satNo,
        CancellationToken cancellationToken)
    {
        var items = await templateService.GetApplicableAsync(
            new FilterTemplateApplicableRequest(taskNo, satNo),
            cancellationToken);
        return Ok(ApiResponse<IReadOnlyCollection<FilterTemplateView>>.Ok(items, HttpContext));
    }

    [HttpGet("{templateId:guid}/versions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<FilterTemplateView>>>> ListVersions(
        Guid templateId,
        CancellationToken cancellationToken)
    {
        try
        {
            var versions = await templateService.GetVersionsAsync(templateId, cancellationToken);
            return Ok(ApiResponse<IReadOnlyCollection<FilterTemplateView>>.Ok(versions, HttpContext));
        }
        catch (TemplateGovernanceException ex)
        {
            return MapError<IReadOnlyCollection<FilterTemplateView>>(ex);
        }
    }

    [HttpGet("{templateId:guid}/versions/{version:int}")]
    public async Task<ActionResult<ApiResponse<FilterTemplateDetail>>> GetVersion(
        Guid templateId,
        int version,
        CancellationToken cancellationToken)
    {
        try
        {
            var detail = await templateService.GetVersionDetailAsync(templateId, version, cancellationToken);
            return Ok(ApiResponse<FilterTemplateDetail>.Ok(detail, HttpContext));
        }
        catch (TemplateGovernanceException ex)
        {
            return MapError<FilterTemplateDetail>(ex);
        }
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<FilterTemplateDetail>>> Create(
        [FromBody] CreateFilterTemplateRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var detail = await templateService.CreateAsync(request, GetOperatorId(), cancellationToken);
            return Ok(ApiResponse<FilterTemplateDetail>.Ok(detail, HttpContext));
        }
        catch (TemplateGovernanceException ex)
        {
            return MapError<FilterTemplateDetail>(ex);
        }
    }

    [HttpPut("{templateId:guid}/versions/{version:int}")]
    public async Task<ActionResult<ApiResponse<FilterTemplateDetail>>> Update(
        Guid templateId,
        int version,
        [FromBody] UpdateFilterTemplateRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var detail = await templateService.UpdateAsync(templateId, version, request, GetOperatorId(), cancellationToken);
            return Ok(ApiResponse<FilterTemplateDetail>.Ok(detail, HttpContext));
        }
        catch (TemplateGovernanceException ex)
        {
            return MapError<FilterTemplateDetail>(ex);
        }
    }

    [HttpPost("{templateId:guid}/versions/{version:int}/publish")]
    public async Task<ActionResult<ApiResponse<FilterTemplateView>>> Publish(
        Guid templateId,
        int version,
        CancellationToken cancellationToken)
    {
        try
        {
            var view = await templateService.PublishAsync(templateId, version, GetOperatorId(), cancellationToken);
            return Ok(ApiResponse<FilterTemplateView>.Ok(view, HttpContext));
        }
        catch (TemplateGovernanceException ex)
        {
            return MapError<FilterTemplateView>(ex);
        }
    }

    [HttpPost("{templateId:guid}/versions/{version:int}/archive")]
    public async Task<ActionResult<ApiResponse<FilterTemplateView>>> Archive(
        Guid templateId,
        int version,
        CancellationToken cancellationToken)
    {
        try
        {
            var view = await templateService.ArchiveAsync(templateId, version, GetOperatorId(), cancellationToken);
            return Ok(ApiResponse<FilterTemplateView>.Ok(view, HttpContext));
        }
        catch (TemplateGovernanceException ex)
        {
            return MapError<FilterTemplateView>(ex);
        }
    }

    [HttpPost("{templateId:guid}/clone")]
    public async Task<ActionResult<ApiResponse<FilterTemplateDetail>>> Clone(
        Guid templateId,
        [FromQuery] int? sourceVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            var detail = await templateService.CloneAsync(templateId, sourceVersion, GetOperatorId(), cancellationToken);
            return Ok(ApiResponse<FilterTemplateDetail>.Ok(detail, HttpContext));
        }
        catch (TemplateGovernanceException ex)
        {
            return MapError<FilterTemplateDetail>(ex);
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

    private Guid? GetOperatorId()
    {
        var userIdClaim = User?.FindFirst("sub")?.Value;
        return Guid.TryParse(userIdClaim, out var id) ? id : null;
    }

    private ActionResult<ApiResponse<T>> MapError<T>(TemplateGovernanceException ex)
    {
        var status = ex.ErrorCode switch
        {
            TemplateErrorCodes.FilterTemplateNotFound => StatusCodes.Status404NotFound,
            TemplateErrorCodes.GroupNotFound => StatusCodes.Status404NotFound,
            TemplateErrorCodes.FilterTemplateNotEditable => StatusCodes.Status409Conflict,
            TemplateErrorCodes.FilterTemplateInvalidState => StatusCodes.Status409Conflict,
            TemplateErrorCodes.FilterTemplateConfigInvalid => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status400BadRequest
        };
        return StatusCode(status, ApiResponse<T>.Fail(ex.ErrorCode, ex.Message, HttpContext));
    }
}
