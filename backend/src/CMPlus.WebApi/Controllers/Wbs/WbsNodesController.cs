using CMPlus.Application.Features.Wbs.Queries.GetNodeActivities;
using CMPlus.WebApi.ErrorHandling;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.Controllers.Wbs;

/// <summary>
/// S4-BE-05 (fast-follow, US-4.5): <c>GET /api/v1/projects/{projectId}/wbs-nodes/{wbsNodeId}/activities</c> -
/// the activity list for one WBS node, the natural complement <see cref="WbsController"/>'s tree
/// read deliberately omits (node-level <c>ActivityCount</c> rollup only). Same authenticated-any-
/// role gate as <see cref="WbsController"/> - this is a read needed by the same batch-progress
/// grid that endpoint feeds.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:guid}/wbs-nodes/{wbsNodeId:guid}")]
public sealed class WbsNodesController(ISender sender) : ControllerBase
{
    [HttpGet("activities")]
    public async Task<IActionResult> GetActivities(Guid projectId, Guid wbsNodeId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetNodeActivitiesQuery(projectId, wbsNodeId), cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }
}
