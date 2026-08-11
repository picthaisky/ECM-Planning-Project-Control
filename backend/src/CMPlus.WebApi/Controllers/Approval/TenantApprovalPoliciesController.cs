using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Approval.Commands.UpdateApprovalPolicy;
using CMPlus.Application.Features.Approval.Queries.GetApprovalPolicy;
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
}
