using CMPlus.Application.Features.Manpower.Queries.ListWorkCategories;
using CMPlus.WebApi.ErrorHandling;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.Controllers.Manpower;

/// <summary>
/// <c>GET /api/v1/projects/{projectId}/work-categories</c> - the work-category catalogue for the
/// Man/Equipment log form's dropdown (the S12 gap: previously the form offered a raw GUID field).
/// Class-level <c>[Authorize]</c> only (any authenticated tenant user can read their own tenant's
/// catalogue - it is reference data, not sensitive), mirroring <c>ProjectsController.GetAll</c>.
/// Never 404s (see <see cref="ListWorkCategoriesQuery"/>).
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:guid}/work-categories")]
public sealed class ProjectWorkCategoriesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid projectId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ListWorkCategoriesQuery(projectId), cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }
}
