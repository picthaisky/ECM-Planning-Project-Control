using CMPlus.Application.Features.Wbs.Queries.GetWbsTree;
using CMPlus.WebApi.ErrorHandling;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.Controllers.Wbs;

/// <summary>
/// S4-BE-01 (US-4.1): the WBS tree read (docs/9 §4: <c>GET /api/v1/projects/{id}/wbs-tree</c>, P95
/// &lt; 100 ms at 5,000 nodes). Thin per clean-architecture-dotnet - dispatches to MediatR and maps
/// the result; the projection/tree-building all lives in Application/Infrastructure.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:guid}/wbs-tree")]
public sealed class WbsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid projectId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetWbsTreeQuery(projectId), cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }
}
