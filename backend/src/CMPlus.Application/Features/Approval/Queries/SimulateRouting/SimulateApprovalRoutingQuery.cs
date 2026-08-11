using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Approval.Queries.SimulateRouting;

/// <summary>
/// S15-BE-01 ("ทดสอบเส้นทางอนุมัติ" - the approval-routing simulator): <c>POST
/// /api/v1/tenants/{tenantId}/approval-policies/{documentType}/simulate</c>. Feeds a hypothetical
/// <paramref name="Amount"/> for a specific, real <paramref name="ProjectId"/> through the exact same
/// resolution path a real Submit uses right now - <see cref="CMPlus.Application.Abstractions.IApprovalPolicyReader.GetCandidatePoliciesAsync"/>
/// then <see cref="CMPlus.Application.Approval.IApprovalRoutingService.Resolve"/> - and returns the
/// chain that would actually be resolved, without creating any document (see
/// <see cref="SimulateApprovalRoutingQueryHandler"/>'s own remarks for why this reuse, rather than a
/// second implementation, is the entire point of the feature).
///
/// <para><paramref name="ProjectId"/> is deliberately mandatory, not optional: every real
/// <c>VariationOrder</c>/<c>PaymentCertificate</c> Submit is always project-scoped (both aggregates
/// carry a non-nullable <c>ProjectId</c>), and a Variation Order's cumulative-VO-escalation test
/// (ADR-0015) needs a real project's <c>EscalationBaselineContractValue</c>/approved-VO total to
/// evaluate at all. A "generic, no project" simulation would silently skip that test for VOs - a
/// real path divergence, exactly the trap this feature exists to avoid - rather than actually
/// simulating "what happens if this project submits this amount right now".</para>
/// </summary>
public sealed record SimulateApprovalRoutingQuery(
    ApprovalDocumentType DocumentType,
    Guid ProjectId,
    decimal Amount) : IRequest<Result<ApprovalRoutingSimulationDto>>;
