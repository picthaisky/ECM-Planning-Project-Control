using CMPlus.Application.Features.Dashboard.Queries.GetDashboard;
using CMPlus.Domain.Enums;
using CMPlus.WebApi.ErrorHandling;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.Controllers.Dashboard;

/// <summary>
/// S8-BE-02 (US-8.1/US-8.3): `GET .../dashboard` - the Executive Dashboard's KPI tiles. Thin per
/// clean-architecture-dotnet - all computation lives in <c>EvmComputationService</c>/
/// <c>WbsProgressRollupCalculator</c>; this controller only routes and maps
/// <see cref="CMPlus.Domain.Common.Result{T}"/> to an <see cref="IActionResult"/>.
///
/// <para><b>Role-gated the same way as <c>EvmController</c>/<c>CashFlowController</c> (ADR-0013),
/// for the same reason:</b> the response carries the project's AC/CV/CPI/EAC/ETC/VAC figures at
/// `Project.EacVariantDefault` - the same margin-bearing shape those two endpoints already gate.
/// Kept identical to <c>EvmController</c>'s role list for consistency, including the same documented
/// <c>Planning</c> trade-off flagged there.</para>
/// </summary>
[ApiController]
[Authorize(Roles = $"{nameof(UserRole.PM)},{nameof(UserRole.QS)},{nameof(UserRole.ProjectDirector)},{nameof(UserRole.Executive)},{nameof(UserRole.Admin)}")]
[Route("api/v1/projects/{projectId:guid}/dashboard")]
public sealed class DashboardController(ISender sender) : ControllerBase
{
    /// <summary><paramref name="dataDate"/> omitted defaults to `Project.DataDate`
    /// (<see cref="GetDashboardQuery"/>'s remarks). The WBS progress rollup tile is unaffected by
    /// this parameter - it always reflects current state.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(
        Guid projectId, [FromQuery] DateTimeOffset? dataDate, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetDashboardQuery(projectId, dataDate), cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }
}
