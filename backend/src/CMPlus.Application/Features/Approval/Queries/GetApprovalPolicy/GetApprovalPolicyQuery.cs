using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Approval.Queries.GetApprovalPolicy;

/// <summary>
/// S2-BE-07: reads the tenant-wide active policy for one document type. Tenant scoping happens
/// entirely via the ambient <c>ITenantProvider</c>/EF global query filter (ADR-0002) - this query
/// deliberately carries no TenantId of its own; the controller has already asserted the route
/// tenantId matches the caller's JWT tenantId before ever dispatching this query (cross-tenant
/// requests never reach here - approval-workflow.md/design.md: "never 403, never leak existence").
/// </summary>
public sealed record GetApprovalPolicyQuery(ApprovalDocumentType DocumentType) : IRequest<Result<ApprovalPolicyDto>>;
