using Microsoft.AspNetCore.Mvc;
using SatelliteData.Application.Templates;

namespace SatelliteData.Api.Controllers;

[ApiController]
[Route("api/v1/asset/groups")]
public sealed class SatelliteGroupsController(SatelliteGroupService groupService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SatelliteGroupNode>>>> GetTree(
        CancellationToken cancellationToken)
    {
        var tree = await groupService.GetTreeAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<SatelliteGroupNode>>.Ok(tree, HttpContext));
    }

    [HttpGet("{groupId:guid}")]
    public async Task<ActionResult<ApiResponse<SatelliteGroupNode>>> Get(
        Guid groupId,
        CancellationToken cancellationToken)
    {
        try
        {
            var node = await groupService.GetByIdAsync(groupId, cancellationToken);
            return Ok(ApiResponse<SatelliteGroupNode>.Ok(node, HttpContext));
        }
        catch (TemplateGovernanceException ex)
        {
            return MapError<SatelliteGroupNode>(ex);
        }
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<SatelliteGroupNode>>> Create(
        [FromBody] CreateSatelliteGroupRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var node = await groupService.CreateAsync(request, cancellationToken);
            return Ok(ApiResponse<SatelliteGroupNode>.Ok(node, HttpContext));
        }
        catch (TemplateGovernanceException ex)
        {
            return MapError<SatelliteGroupNode>(ex);
        }
    }

    [HttpPut("{groupId:guid}")]
    public async Task<ActionResult<ApiResponse<SatelliteGroupNode>>> Update(
        Guid groupId,
        [FromBody] UpdateSatelliteGroupRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var node = await groupService.UpdateAsync(groupId, request, cancellationToken);
            return Ok(ApiResponse<SatelliteGroupNode>.Ok(node, HttpContext));
        }
        catch (TemplateGovernanceException ex)
        {
            return MapError<SatelliteGroupNode>(ex);
        }
    }

    [HttpDelete("{groupId:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> Delete(
        Guid groupId,
        CancellationToken cancellationToken)
    {
        try
        {
            await groupService.DeleteAsync(groupId, cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { deleted = true }, HttpContext));
        }
        catch (TemplateGovernanceException ex)
        {
            return MapError<object>(ex);
        }
    }

    [HttpGet("{groupId:guid}/members")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<SatelliteGroupMemberDto>>>> GetMembers(
        Guid groupId,
        [FromQuery] bool includeDescendants = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var members = await groupService.GetMembersAsync(groupId, includeDescendants, cancellationToken);
            return Ok(ApiResponse<IReadOnlyCollection<SatelliteGroupMemberDto>>.Ok(members, HttpContext));
        }
        catch (TemplateGovernanceException ex)
        {
            return MapError<IReadOnlyCollection<SatelliteGroupMemberDto>>(ex);
        }
    }

    [HttpPost("{groupId:guid}/members")]
    public async Task<ActionResult<ApiResponse<object>>> AddMembers(
        Guid groupId,
        [FromBody] AddGroupMembersRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await groupService.AddMembersAsync(groupId, request, cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { added = request.Satellites.Count }, HttpContext));
        }
        catch (TemplateGovernanceException ex)
        {
            return MapError<object>(ex);
        }
    }

    [HttpDelete("{groupId:guid}/members/{tasookNo}/{satelliteNo}")]
    public async Task<ActionResult<ApiResponse<object>>> RemoveMember(
        Guid groupId,
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken)
    {
        try
        {
            await groupService.RemoveMemberAsync(groupId, tasookNo, satelliteNo, cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { removed = true }, HttpContext));
        }
        catch (TemplateGovernanceException ex)
        {
            return MapError<object>(ex);
        }
    }

    private ActionResult<ApiResponse<T>> MapError<T>(TemplateGovernanceException ex)
    {
        var status = ex.ErrorCode switch
        {
            TemplateErrorCodes.GroupNotFound => StatusCodes.Status404NotFound,
            TemplateErrorCodes.GroupDeleteRefused => StatusCodes.Status409Conflict,
            TemplateErrorCodes.GroupSiblingNameDuplicated => StatusCodes.Status409Conflict,
            TemplateErrorCodes.GroupCircular => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        return StatusCode(status, ApiResponse<T>.Fail(ex.ErrorCode, ex.Message, HttpContext));
    }
}
