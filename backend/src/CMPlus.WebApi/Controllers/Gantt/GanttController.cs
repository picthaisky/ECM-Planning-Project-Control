using CMPlus.Application.Features.Gantt.Queries.GetGantt;
using CMPlus.WebApi.ErrorHandling;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.Controllers.Gantt;

/// <summary>
/// S6-BE-01 (US-6.1): the Gantt chart read (docs/9 §4: <c>GET /api/v1/projects/{id}/gantt</c>).
/// Thin per clean-architecture-dotnet - dispatches to MediatR and maps the result; the
/// projection/row-ordering all lives in Application/Infrastructure. Plain <c>[Authorize]</c> with no
/// role restriction, same as <c>WbsController</c> - viewing the schedule is a read available to any
/// authenticated tenant user, unlike <c>CpmController</c>'s recalculate action which is a planning
/// mutation gated to PM/Planning/Admin.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:guid}/gantt")]
public sealed class GanttController(ISender sender) : ControllerBase
{
    /// <summary><paramref name="from"/>/<paramref name="to"/> are the optional, independently
    /// one-sided date-range window (S6-BE-01 "รองรับ query ตามช่วงวันที่") - see
    /// <see cref="GetGanttQuery"/>'s remarks for exactly what they filter.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(
        Guid projectId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetGanttQuery(projectId, from, to), cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }
}
