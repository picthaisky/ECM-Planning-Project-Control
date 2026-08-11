using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Approval.Commands.UpdateApprovalPolicy;
using CMPlus.Application.Features.Approval.Queries.GetApprovalPolicy;
using CMPlus.Application.Features.Approval.Queries.GetApprovalPolicyVersionHistory;
using CMPlus.Application.Features.Approval.Queries.SimulateRouting;
using CMPlus.Domain.Enums;
using CMPlus.WebApi.ErrorHandling;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.Controllers.Approval;

/// <summary>
/// S2-BE-07/S9-BE-06: tenant approval-policy read + write, Admin-only end to end. A route
/// <c>tenantId</c> that does not match the caller's own JWT tenant claim always returns a bare 404
/// - never 403, never a body that would let a caller distinguish "wrong tenant" from "tenant does
/// not exist" from "no policy configured yet" (design.md §2.2: "Cross-tenant request -&gt; 404
/// (never 403 - do not confirm another tenant exists)"; this doubles as the DoD proof that the
/// JWT's tenantId claim - not the route value - is what actually scopes every command/query here,
/// since a trusting implementation would instead attempt (and could leak/corrupt) another tenant's
/// data here).
/// </summary>
[ApiController]
[Route("api/v1/tenants/{tenantId:guid}/approval-policies")]
[Authorize(Roles = nameof(UserRole.Admin))]
public sealed class TenantApprovalPoliciesController(ISender sender, ITenantProvider tenantProvider) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        Guid tenantId, [FromQuery] ApprovalDocumentType documentType, CancellationToken cancellationToken)
    {
        if (tenantId != tenantProvider.TenantId)
        {
            return NotFound();
        }

        var result = await sender.Send(new GetApprovalPolicyQuery(documentType), cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    /// <summary>S9-BE-06: always creates <c>Version+1</c> and deactivates the previous version -
    /// never edits in place (ADR-0008). <see cref="tenantId"/> not matching the caller's own JWT
    /// tenant claim returns a bare 404, identically to <see cref="Get"/> above.</summary>
    [HttpPut("{documentType}")]
    public async Task<IActionResult> Update(
        Guid tenantId,
        ApprovalDocumentType documentType,
        [FromBody] UpdateApprovalPolicyRequest request,
        CancellationToken cancellationToken)
    {
        if (tenantId != tenantProvider.TenantId)
        {
            return NotFound();
        }

        var command = new UpdateApprovalPolicyCommand(
            documentType,
            request.AllowSelfApproval,
            request.CumulativeVoEscalationPct,
            request.CumulativeVoEscalationRole,
            request.Rules.Select(r => r.ToDomainInput()).ToList());

        var result = await sender.Send(command, cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    /// <summary>S15-BE-01: "ประวัติเวอร์ชัน" - the full version timeline for the tenant-wide default
    /// policy of <paramref name="documentType"/>, read from <c>ApprovalPolicy</c> + <c>AuditLog</c>
    /// (no new storage). Same cross-tenant 404 discipline as <see cref="Get"/> above.</summary>
    [HttpGet("{documentType}/history")]
    public async Task<IActionResult> GetHistory(
        Guid tenantId, ApprovalDocumentType documentType, CancellationToken cancellationToken)
    {
        if (tenantId != tenantProvider.TenantId)
        {
            return NotFound();
        }

        var result = await sender.Send(new GetApprovalPolicyVersionHistoryQuery(documentType), cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    /// <summary>S15-BE-01: "ทดสอบเส้นทางอนุมัติ" - resolves the chain a real Submit would produce right
    /// now for a hypothetical amount against a real project, WITHOUT creating any document (see
    /// <see cref="SimulateApprovalRoutingQueryHandler"/>'s own remarks). A read-only action, hence
    /// <c>POST</c> rather than a CRUD-shaped route - mirrors <c>CpmController.Recalculate</c>/
    /// <c>ProjectEotEvaluationsController.Evaluate</c>'s existing "action endpoint" convention in this
    /// codebase - returning <c>200 Ok</c>, never <c>201 Created</c>, since nothing is created. Same
    /// cross-tenant 404 discipline as <see cref="Get"/> above.</summary>
    [HttpPost("{documentType}/simulate")]
    public async Task<IActionResult> Simulate(
        Guid tenantId,
        ApprovalDocumentType documentType,
        [FromBody] SimulateApprovalRoutingRequest request,
        CancellationToken cancellationToken)
    {
        if (tenantId != tenantProvider.TenantId)
        {
            return NotFound();
        }

        var query = new SimulateApprovalRoutingQuery(documentType, request.ProjectId, request.Amount);
        var result = await sender.Send(query, cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }
}
