using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Approval.Queries.GetApprovalPolicy;
using CMPlus.Domain.Enums;
using CMPlus.WebApi.ErrorHandling;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.Controllers.Approval;

/// <summary>
/// S2-BE-07: read-only tenant approval-policy lookup, Admin-only. A route <c>tenantId</c> that
/// does not match the caller's own JWT tenant claim always returns a bare 404 - never 403, never a
/// body that would let a caller distinguish "wrong tenant" from "tenant does not exist" from "no
/// policy configured yet" (design.md §2.2: "Cross-tenant request -&gt; 404 (never 403 - do not
/// confirm another tenant exists)"; this doubles as the DoD proof that the JWT's tenantId claim -
/// not the route value - is what actually scopes the query, since a trusting implementation would
/// instead attempt (and could leak) another tenant's data here).
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
}
