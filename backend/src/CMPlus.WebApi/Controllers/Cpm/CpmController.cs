using CMPlus.Application.Features.Cpm.Commands.RecalculateCpm;
using CMPlus.Domain.Enums;
using CMPlus.WebApi.ErrorHandling;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.Controllers.Cpm;

/// <summary>
/// S5-BE-04 (US-5.1): the WBS/Gantt screen's "คำนวณ CPM ใหม่" (recalculate CPM) action that
/// S5-FE-01 (Sprint 5 frontend row) calls. Role gate is PM/Planning/Admin - scheduling recalculation
/// is a planning operation, not something Site/QS ever trigger (mirrors the role reasoning already
/// used for <c>UpdateProjectCommand</c>'s master-data edits).
/// </summary>
[ApiController]
[Authorize(Roles = $"{nameof(UserRole.PM)},{nameof(UserRole.Planning)},{nameof(UserRole.Admin)}")]
[Route("api/v1/projects/{projectId:guid}/cpm")]
public sealed class CpmController(ISender sender) : ControllerBase
{
    [HttpPost("recalculate")]
    public async Task<IActionResult> Recalculate(Guid projectId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RecalculateCpmCommand(projectId), cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }
}
