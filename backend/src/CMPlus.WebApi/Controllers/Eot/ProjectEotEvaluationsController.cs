using CMPlus.Application.Features.Eot.Commands.EvaluateEot;
using CMPlus.Domain.Enums;
using CMPlus.WebApi.ErrorHandling;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.Controllers.Eot;

/// <summary>
/// S11-BE-02 (US-11.1): triggers one <c>EotEvaluator</c> run and returns the persisted, immutable
/// <c>EotEvaluation</c> (domain-rules.md weather-eot §8.5). There is deliberately no
/// <c>PUT</c>/<c>PATCH</c>/<c>DELETE</c> - re-evaluating is always a new <c>POST</c>, never an edit
/// (§8.5 rule 3: "the remedy is always a new evaluation record").
///
/// <para><b>Role gate.</b> PM/Planning/QS/Admin - the roles that prepare or review a schedule-impact/
/// EOT analysis, mirroring <c>CpmController</c>'s PM/Planning/Admin scheduling gate plus QS (who owns
/// claim preparation - domain-rules.md §2.2's entitlement disclosure is squarely QS/PM territory).
/// Narrower than <c>ProjectWeatherLogsController</c>'s read gate deliberately: this produces an
/// analytical opinion about the schedule, not raw site data, so <see cref="UserRole.Site"/> is
/// excluded.</para>
/// </summary>
[ApiController]
[Authorize(Roles = $"{nameof(UserRole.PM)},{nameof(UserRole.Planning)},{nameof(UserRole.QS)},{nameof(UserRole.Admin)}")]
[Route("api/v1/projects/{projectId:guid}/eot-evaluations")]
public sealed class ProjectEotEvaluationsController(ISender sender) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Evaluate(
        Guid projectId, [FromBody] EvaluateEotRequest? request, CancellationToken cancellationToken)
    {
        var command = new EvaluateEotCommand(projectId, request?.WindowStart, request?.WindowEnd);
        var result = await sender.Send(command, cancellationToken);

        return result.IsSuccess
            ? Created($"/api/v1/projects/{projectId}/eot-evaluations/{result.Value.Id}", result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }
}
