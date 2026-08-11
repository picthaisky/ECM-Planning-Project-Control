using CMPlus.Application.Features.Approval.Queries.GetApprovalPolicy;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Approval.Commands.UpdateApprovalPolicy;

/// <summary>
/// <c>PUT /api/v1/tenants/{tenantId}/approval-policies/{documentType}</c> (S9-BE-06, design.md
/// §2.2, ADR-0008): always creates a brand-new <c>Version+1</c> row and deactivates the previous
/// one - never edits a policy in place. Documents already <c>PendingApproval</c> keep the version
/// that routed them (they pinned <c>ApprovalPolicyId</c>/<c>ApprovalPolicyVersion</c> at Submit
/// time and re-resolve authority against that exact row via
/// <c>IApprovalPolicyReader.GetByIdAsync</c>, never against "the currently active policy") - this
/// command can never retroactively change how an in-flight document is approved.
///
/// <para>Reuses the Domain's own <see cref="ApprovalPolicyRuleInput"/> shape directly (no separate
/// Application-level rule DTO) and the existing read-side <see cref="ApprovalPolicyDto"/>/
/// <see cref="ApprovalPolicyRuleDto"/> (S2-BE-07) as its response shape - "the current
/// representation of the resource", the same convention every other command in this codebase
/// follows.</para>
/// </summary>
public sealed record UpdateApprovalPolicyCommand(
    ApprovalDocumentType DocumentType,
    bool AllowSelfApproval,
    decimal? CumulativeVoEscalationPct,
    UserRole? CumulativeVoEscalationRole,
    IReadOnlyList<ApprovalPolicyRuleInput> Rules) : IRequest<Result<ApprovalPolicyDto>>;
