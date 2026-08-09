using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Approval.Queries.GetApprovalActionHistory;

/// <summary>
/// `GET /api/v1/{payment-certificates|variation-orders}/{id}/approval-actions` (design.md §2.3;
/// S9 read-side gap closure, finding L-04) - the append-only approval-history ledger for one
/// document. Deliberately keyed on <see cref="ApprovalDocumentType"/> + <see cref="DocumentId"/>,
/// never on a document-specific id type, mirroring
/// <see cref="CMPlus.Application.Abstractions.IApprovalActionRepository.GetHistoryAsync"/>'s own
/// document-type-agnostic shape - this query/handler is a thin wrapper around that repository
/// method and is reused unchanged by Sprint 10's VariationOrder controller (a second, near-identical
/// `[HttpGet("{id:guid}/approval-actions")]` action dispatching
/// <c>new GetApprovalActionHistoryQuery(ApprovalDocumentType.VariationOrder, id)</c>), not a
/// per-document-type rewrite.
///
/// <para>This history is evidence: the people the approval chain protects (creator, submitter,
/// approvers, and anyone auditing a disputed certificate) currently cannot inspect it at all -
/// `ApprovalChainBar.tsx` has been built and waiting on this endpoint since Sprint 9.</para>
/// </summary>
public sealed record GetApprovalActionHistoryQuery(ApprovalDocumentType DocumentType, Guid DocumentId)
    : IRequest<Result<IReadOnlyList<ApprovalActionDto>>>;
