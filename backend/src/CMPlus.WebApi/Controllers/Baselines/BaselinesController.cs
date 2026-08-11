using CMPlus.Application.Features.Baseline.Commands.ActivateBaseline;
using CMPlus.Application.Features.Baseline.Commands.CaptureBaseline;
using CMPlus.Application.Features.Baseline.Queries.CompareBaseline;
using CMPlus.Domain.Enums;
using CMPlus.WebApi.ErrorHandling;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.Controllers.Baselines;

/// <summary>
/// S14-BE-01/02 (US-14.1/14.2). Two role gates on one controller, deliberately (mirrors
/// <c>ProjectsController</c>'s identical "class-level [Authorize], method-level Roles where they
/// differ" reasoning):
/// <list type="bullet">
/// <item><see cref="Capture"/>/<see cref="Activate"/> are PM/Planning/Admin - the same audience
/// <c>CpmController</c>'s "คำนวณ CPM ใหม่" gate already uses, since capturing/activating a baseline
/// is a scheduling/reference-setting operation over the same schedule data, not something Site/QS/
/// Executive trigger.</item>
/// <item><see cref="Compare"/> is PM/QS/ProjectDirector/Executive/Admin - kept identical to
/// <c>EvmController</c>/<c>CashFlowController</c>'s role list, because the response carries
/// <c>BudgetCost</c>/<c>BAC</c> figures (the same commercially-sensitive shape those two endpoints
/// already gate), not merely schedule dates. This is a documented trade-off, not a settled one:
/// Planning owns schedule-drift tracking day to day in real practice and is excluded here purely
/// because of the embedded money columns - the same caveat <c>CashFlowController</c> already flags
/// for its own case, inherited rather than re-litigated. <c>po-analyst</c>/<c>domain-expert</c>
/// should confirm whether this response's schedule-only fields warrant a wider audience later.
/// </item>
/// </list>
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:guid}/baselines")]
public sealed class BaselinesController(ISender sender) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = $"{nameof(UserRole.PM)},{nameof(UserRole.Planning)},{nameof(UserRole.Admin)}")]
    public async Task<IActionResult> Capture(
        Guid projectId, [FromBody] CaptureBaselineRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CaptureBaselineCommand(projectId, request.Name), cancellationToken);

        return result.IsSuccess
            ? Created($"/api/v1/projects/{projectId}/baselines/{result.Value.Id}", result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    [HttpPost("{baselineId:guid}/activate")]
    [Authorize(Roles = $"{nameof(UserRole.PM)},{nameof(UserRole.Planning)},{nameof(UserRole.Admin)}")]
    public async Task<IActionResult> Activate(Guid projectId, Guid baselineId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ActivateBaselineCommand(projectId, baselineId), cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    /// <summary><paramref name="baselineId"/> omitted compares against the project's currently-active
    /// baseline (see <see cref="CompareBaselineQuery"/>'s remarks); supplying it targets that
    /// specific baseline instead.</summary>
    [HttpGet("compare")]
    [Authorize(Roles = $"{nameof(UserRole.PM)},{nameof(UserRole.QS)},{nameof(UserRole.ProjectDirector)},{nameof(UserRole.Executive)},{nameof(UserRole.Admin)}")]
    public async Task<IActionResult> Compare(Guid projectId, [FromQuery] Guid? baselineId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CompareBaselineQuery(projectId, baselineId), cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }
}
