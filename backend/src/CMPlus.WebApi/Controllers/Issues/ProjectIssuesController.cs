using CMPlus.Application.Features.Issues.Commands.AdvanceIssueStatus;
using CMPlus.Application.Features.Issues.Commands.CreateIssue;
using CMPlus.Application.Features.Issues.Queries.ListIssues;
using CMPlus.Domain.Enums;
using CMPlus.WebApi.ErrorHandling;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.Controllers.Issues;

/// <summary>
/// S11-BE-03 (US-11.2): <c>GET/POST /api/v1/projects/{projectId}/issues</c> and
/// <c>POST /api/v1/projects/{projectId}/issues/{issueId}/advance-status</c> - all three actions
/// nested under the project route (domain-rules.md weather-eot §9.1: "The single verb is
/// <c>POST /api/v1/projects/{id}/issues/{issueId}/advance-status</c>", superseding this task's
/// earlier, flat-route judgement call - VO/IPC's flat-action-controller precedent - made before that
/// document landed).
///
/// <para>Role-gating mirrors <see cref="Weather.ProjectWeatherLogsController"/>'s identical
/// reasoning: <see cref="WriteRoles"/> (PM/Planning/Site/QS/Admin) is the established
/// site-data-capture write role set, reads are open to any authenticated tenant member (bare
/// <see cref="AuthorizeAttribute"/>, no role restriction) since issue data is not commercially
/// sensitive.</para>
/// </summary>
[ApiController]
[Route("api/v1/projects/{projectId:guid}/issues")]
public sealed class ProjectIssuesController(ISender sender) : ControllerBase
{
    internal const string WriteRoles =
        $"{nameof(UserRole.PM)},{nameof(UserRole.Planning)},{nameof(UserRole.Site)},{nameof(UserRole.QS)},{nameof(UserRole.Admin)}";

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> List(Guid projectId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ListIssuesQuery(projectId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    [HttpPost]
    [Authorize(Roles = WriteRoles)]
    public async Task<IActionResult> Create(Guid projectId, [FromBody] CreateIssueRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new CreateIssueCommand(projectId, request.Title, request.Detail, request.Owner, request.DueDate),
            cancellationToken);

        return result.IsSuccess
            ? Created($"/api/v1/projects/{projectId}/issues", result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    [HttpPost("{issueId:guid}/advance-status")]
    [Authorize(Roles = WriteRoles)]
    public async Task<IActionResult> AdvanceStatus(Guid projectId, Guid issueId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new AdvanceIssueStatusCommand(projectId, issueId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }
}
