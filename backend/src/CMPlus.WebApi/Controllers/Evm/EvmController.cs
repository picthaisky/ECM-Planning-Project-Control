using CMPlus.Application.Features.Evm.Commands.CloseEvmPeriod;
using CMPlus.Application.Features.Evm.Queries.GetEvm;
using CMPlus.Application.Features.Evm.Queries.ListEvmSnapshots;
using CMPlus.Domain.Enums;
using CMPlus.WebApi.ErrorHandling;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.Controllers.Evm;

/// <summary>
/// S7-BE-03/05 (US-7.1/7.2/7.4*): the EVM S-Curve screen's read (`GET .../evm`) and period-close
/// (`POST .../evm/snapshots`) endpoints (design.md §2.1). Thin per clean-architecture-dotnet - all
/// computation lives in <c>CMPlus.Application.Services.Evm</c>; this controller only routes and maps
/// <see cref="CMPlus.Domain.Common.Result{T}"/> to an <see cref="IActionResult"/>.
///
/// <para><b>Role-gated as of ADR-0013.</b> These actions were originally a bare <c>[Authorize]</c>
/// (any authenticated tenant user), which was correct at the time: <c>AC</c> was pinned at <c>0</c>
/// by the placeholder <c>IActualCostReader</c>, so nothing here revealed cost. Once the real
/// <c>ActualCostEntry</c> ledger landed, that reasoning expired — on a contractor-side tenant
/// (ADR-0013(c)) <c>AC</c>, <c>CV</c>, <c>CPI</c> and every EAC variant together expose the
/// contractor's own spend against the contract price, i.e. its margin. That is not a
/// every-authenticated-user read, and <c>Site</c> in particular has no business need for it. Gated
/// to the same financially-trusted roles that may record cost
/// (<c>ActualCostsController</c>) plus <c>Executive</c>, who needs project financial health by
/// definition.</para>
///
/// <para><b>Known trade-off, needs product confirmation:</b> the EVM response mixes cost metrics
/// (AC/CV/CPI/EAC) with *schedule* metrics (PV/EV/SV/SPI), and gating the whole endpoint therefore
/// also denies <c>Planning</c> the schedule half, which it has a legitimate need for. Gating
/// fail-closed is the right default while the question is open — leaking margin is far worse than
/// an over-restricted read — but the likely correct end state is a finer split that returns
/// schedule metrics to <c>Planning</c> with the cost fields omitted. Flagged for
/// <c>security-auditor</c>/<c>po-analyst</c>.</para>
///
/// <para><see cref="CloseSnapshot"/> is gated the same way: freezing a reporting period produces the
/// figures downstream payment certification cites, which is at least as sensitive as reading them,
/// and it mirrors <c>SetEacVariantDefault</c>'s explicit ADR-0007(f) PM/QS/Executive restriction.</para>
/// </summary>
[ApiController]
[Authorize(Roles = $"{nameof(UserRole.PM)},{nameof(UserRole.QS)},{nameof(UserRole.ProjectDirector)},{nameof(UserRole.Executive)},{nameof(UserRole.Admin)}")]
[Route("api/v1/projects/{projectId:guid}/evm")]
public sealed class EvmController(ISender sender) : ControllerBase
{
    /// <summary><paramref name="dataDate"/> omitted defaults to `Project.DataDate`
    /// (<see cref="GetEvmQuery"/>'s remarks). <paramref name="eacVariant"/> omitted defaults to
    /// `Project.EacVariantDefault`; an unrecognised value is a `400 invalid-eac-variant`, never a
    /// silent fallback.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(
        Guid projectId,
        [FromQuery] DateTimeOffset? dataDate,
        [FromQuery] string? eacVariant,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetEvmQuery(projectId, dataDate, eacVariant), cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    /// <summary>Freezes the live computation at <see cref="CloseEvmPeriodRequest.DataDate"/> into an
    /// immutable <c>EvmPeriodSnapshot</c>. Closing the same data date twice -&gt; 409.</summary>
    [HttpPost("snapshots")]
    public async Task<IActionResult> CloseSnapshot(
        Guid projectId, [FromBody] CloseEvmPeriodRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CloseEvmPeriodCommand(projectId, request.DataDate), cancellationToken);

        if (result.IsFailure)
        {
            return ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
        }

        // There is still no GET-by-snapshot-*id* route (nothing needs one yet); Location points at
        // the closed-period listing below, which is where the newly created snapshot is actually
        // observable.
        return CreatedAtAction(nameof(ListSnapshots), new { projectId }, result.Value);
    }

    /// <summary>
    /// Closed-period history, ascending by data date; `from`/`to` are optional and inclusive.
    ///
    /// <para>This is what S7-FE-04's S-Curve reads for every point *before* the data date. Those
    /// points must come from frozen snapshots rather than a live recomputation: under ADR-0009 a
    /// backdated progress entry legitimately changes what a live recomputation returns for a past
    /// date, so anything that has to stay reproducible cites a snapshot instead.</para>
    /// </summary>
    [HttpGet("snapshots")]
    public async Task<IActionResult> ListSnapshots(
        Guid projectId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ListEvmSnapshotsQuery(projectId, from, to), cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }
}
