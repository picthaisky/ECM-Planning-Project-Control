using CMPlus.Application.Features.Projects.Commands.SetEacVariantDefault;
using CMPlus.Application.Features.Projects.Commands.UpdateProject;
using CMPlus.Application.Features.Projects.Queries.GetProjects;
using CMPlus.Domain.Enums;
using CMPlus.WebApi.ErrorHandling;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.Controllers.Projects;

/// <summary>
/// S4-BE-02 (US-4.3/4.4): edits the Project Info screen's master data. S4-BE-04 (fast-follow, gap
/// closure) adds <c>GET /api/v1/projects</c> - project discovery - to this same controller (the
/// route moved from a fixed <c>{projectId:guid}</c>-suffixed template down to a per-action one so
/// both the collection route and the single-resource route can live here).
///
/// <para><b>Role gate: two different gates on one controller, deliberately.</b> <see cref="Update"/>
/// keeps its interim role restriction (pending a full po-analyst/domain-expert permission-matrix
/// review - same caveat as <c>ImportController</c>'s S3-SEC-01 M-04 decision): US-4.3's persona is
/// the Project Manager, US-4.4's is the QS/Cost Engineer; <see cref="UserRole.Executive"/> and
/// <see cref="UserRole.Admin"/> are included as the other roles plausibly responsible for
/// project/contract setup, and <see cref="UserRole.Site"/>/<see cref="UserRole.Planning"/> are
/// excluded - editing commercial terms (Retention/Advance rate, BAC) is not a field-engineering
/// concern. <see cref="GetAll"/> is deliberately any authenticated role, not narrowed the same way:
/// project *discovery* (which projects exist, so a user can pick one to work in) is a navigation
/// concern every role needs - <c>RequireProject</c>/<c>useProjectStore</c> on the frontend gate
/// *every* <c>/app/:projectId/*</c> route on having resolved a current project id, for Site/
/// Planning users too, not only PM/QS/Executive/Admin - and the response carries no commercial-
/// terms data (see <see cref="ProjectListItemDto"/>'s remarks), so there is no sensitive-data
/// reason to narrow it. Achieved by moving the <c>Roles</c> restriction onto <see cref="Update"/>
/// itself rather than the class: ASP.NET Core combines a class-level and a method-level
/// <c>[Authorize]</c> attribute as a logical AND, so a bare class-level <c>[Authorize]</c> (any
/// authenticated user) plus a method-level <c>Roles</c> gate on <see cref="Update"/> only is both
/// correct and the minimal change - a bare method-level <c>[Authorize]</c> on <see cref="GetAll"/>
/// would <i>not</i> have loosened a class-level <c>Roles</c> restriction had one remained.</para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/projects")]
public sealed class ProjectsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var projects = await sender.Send(new GetProjectsQuery(), cancellationToken);
        return Ok(projects);
    }

    [HttpPut("{projectId:guid}")]
    [Authorize(Roles = $"{nameof(UserRole.PM)},{nameof(UserRole.QS)},{nameof(UserRole.Executive)},{nameof(UserRole.Admin)}")]
    public async Task<IActionResult> Update(
        Guid projectId, [FromBody] UpdateProjectRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateProjectCommand(
            projectId,
            request.Name,
            request.Code,
            request.Owner,
            request.ContractStart,
            request.ContractFinish,
            request.Bac,
            request.ContractValue,
            request.RetentionRate,
            request.AdvanceRate,
            request.RetentionCapPercentage,
            request.RetentionRelease1Percentage,
            request.DefectsLiabilityMonths,
            request.AdvanceAmountPaid,
            request.AdvanceRecoveryMethod,
            request.AdvanceRecoveryStartPct,
            request.AdvanceRecoveryRatePct,
            request.AdvanceRecoveryEndPct);

        var result = await sender.Send(command, cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    /// <summary>
    /// S7-BE-04 (ADR-0007(f)): sets `Project.EacVariantDefault`. Restricted to PM/QS/Executive
    /// (deliberately narrower than <see cref="Update"/>'s PM/QS/Executive/Admin - ADR-0007(f) names
    /// exactly these three roles and does not mention Admin, so Admin is excluded here rather than
    /// carried over from <see cref="Update"/>'s gate by assumption). Audited automatically by
    /// <c>AuditSaveChangesInterceptor</c> (old/new value) - see
    /// <see cref="SetEacVariantDefaultCommand"/>'s remarks.
    /// </summary>
    [HttpPut("{projectId:guid}/eac-default")]
    [Authorize(Roles = $"{nameof(UserRole.PM)},{nameof(UserRole.QS)},{nameof(UserRole.Executive)}")]
    public async Task<IActionResult> SetEacDefault(
        Guid projectId, [FromBody] SetEacVariantDefaultRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new SetEacVariantDefaultCommand(projectId, request.Variant), cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }
}
