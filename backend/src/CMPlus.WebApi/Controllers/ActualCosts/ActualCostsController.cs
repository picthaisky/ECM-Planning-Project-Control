using CMPlus.Application.Features.ActualCosts.Commands.RecordActualCost;
using CMPlus.Domain.Enums;
using CMPlus.WebApi.ErrorHandling;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.Controllers.ActualCosts;

/// <summary>
/// actual-cost.md §9/§12/§10.4 (ADR-0013): the QS manual cost-entry write path - a single entry
/// per request. Excel/CSV import and the QS grid UI are explicitly later work; there is
/// deliberately no <c>GET</c> listing endpoint yet either (out of this task's scope).
///
/// <para>RBAC per actual-cost.md §10.4: cost data is commercially sensitive (margin-bearing).
/// <c>QS</c>/<c>PM</c>/<c>ProjectDirector</c>/<c>Admin</c> may post; <c>Executive</c> is
/// recommended read-only (no read endpoint exists yet to gate) and <c>Site</c> must not read or
/// post cost at all - excluded here accordingly. This does <b>not</b> retrofit the existing
/// <c>GET .../evm</c> endpoint, which today has no role restriction at all and, now that real AC
/// flows through it, is a pre-existing RBAC gap this task surfaces rather than fixes - see the
/// backend-developer report for why that is flagged for <c>security-auditor</c> rather than
/// changed here.</para>
/// </summary>
[ApiController]
[Authorize(Roles = $"{nameof(UserRole.QS)},{nameof(UserRole.PM)},{nameof(UserRole.ProjectDirector)},{nameof(UserRole.Admin)}")]
[Route("api/v1/projects/{projectId:guid}/actual-costs")]
public sealed class ActualCostsController(ISender sender) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Record(
        Guid projectId, [FromBody] RecordActualCostRequest request, CancellationToken cancellationToken)
    {
        var command = new RecordActualCostCommand(
            projectId,
            request.WbsNodeId,
            request.ActivityId,
            request.CostCategory,
            request.EntryType,
            request.Amount,
            request.IncurredDate,
            request.ReversesEntryId,
            request.DocumentReference,
            request.CostCode,
            request.VendorName,
            request.Note,
            request.PaidDate,
            request.Quantity,
            request.UnitOfMeasure);

        var result = await sender.Send(command, cancellationToken);

        // No GET-by-id route exists yet to CreatedAtAction against (this task's scope is the write
        // path only) - the collection URI itself is still the correct Location per RFC 9110 §10.2.2
        // ("SHOULD be one that most closely represents... the newly created resource").
        return result.IsSuccess
            ? Created($"/api/v1/projects/{projectId}/actual-costs", result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }
}
