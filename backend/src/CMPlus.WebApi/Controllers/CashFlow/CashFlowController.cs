using CMPlus.Application.Features.CashFlow.Queries.GetCashFlow;
using CMPlus.Domain.Enums;
using CMPlus.WebApi.ErrorHandling;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.Controllers.CashFlow;

/// <summary>
/// S8-BE-01 (US-8.2): `GET .../cash-flow` - period bars + cumulative summary, sourced entirely from
/// the EVM engine (see <c>GetCashFlowQueryHandler</c>'s remarks). Thin per clean-architecture-dotnet -
/// no computation lives here.
///
/// <para><b>Role-gated the same way as <c>EvmController</c> (ADR-0013), for the same reason:</b> this
/// response's <c>acCumulative</c>/period AC figures expose the contractor's own spend, i.e. margin
/// once set against the (currently absent) receipts side - the same commercially-sensitive shape
/// <c>EvmController</c>/<c>ActualCostsController</c> already gate. Kept identical to
/// <c>EvmController</c>'s role list (<c>PM</c>/<c>QS</c>/<c>ProjectDirector</c>/<c>Executive</c>/
/// <c>Admin</c>) for consistency rather than re-deriving a separate policy - including the same
/// documented <c>Planning</c> trade-off flagged there for <c>security-auditor</c>/<c>po-analyst</c>,
/// not silently resolved differently here.</para>
/// </summary>
[ApiController]
[Authorize(Roles = $"{nameof(UserRole.PM)},{nameof(UserRole.QS)},{nameof(UserRole.ProjectDirector)},{nameof(UserRole.Executive)},{nameof(UserRole.Admin)}")]
[Route("api/v1/projects/{projectId:guid}/cash-flow")]
public sealed class CashFlowController(ISender sender) : ControllerBase
{
    /// <summary><paramref name="dataDate"/> omitted defaults to `Project.DataDate` and is also the
    /// upper bound of the period-bars window (<see cref="GetCashFlowQuery"/>'s remarks).
    /// <paramref name="from"/> omitted means "since project inception".</summary>
    [HttpGet]
    public async Task<IActionResult> Get(
        Guid projectId,
        [FromQuery] DateTimeOffset? dataDate,
        [FromQuery] DateTimeOffset? from,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetCashFlowQuery(projectId, dataDate, from), cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }
}
