using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// Persistence boundary for the append-only <see cref="ApprovalAction"/> ledger (ADR-0008,
/// approval-workflow.md §5.2/§6.6). Deliberately document-type-agnostic (keyed on
/// <see cref="ApprovalDocumentType"/> + document id, not on <c>PaymentCertificate</c>
/// specifically) so the identical Sprint 10 <c>VariationOrder</c> transition handlers can reuse it
/// unchanged, per ADR-0008's own staging note that Sprint 10 "carries no rework" from Sprint 9.
/// </summary>
public interface IApprovalActionRepository
{
    /// <summary>Stages one new, immutable <see cref="ApprovalAction"/> row - persisted by whichever
    /// aggregate repository's <c>TrySaveChangesAsync</c>/<c>SaveChangesAsync</c> commits this same
    /// request's unit of work (both share the one scoped <c>DbContext</c> per request). There is no
    /// update/delete method on this interface, matching the entity's own append-only shape.</summary>
    void Add(ApprovalAction action);

    /// <summary>Every <see cref="ApprovalAction"/> row for this document, across every revision,
    /// oldest-<see cref="ApprovalAction.ActedAt"/>-first - tenant-scoped by the global EF query
    /// filter (ADR-0002). Callers filter to the revision/step/actor they care about in memory (the
    /// row count per document is always small), which keeps this interface's read surface to a
    /// single, reusable method rather than one bespoke query per business rule (self-approval,
    /// "one human cannot satisfy two steps of the same chain", the eventual approval-actions history
    /// endpoint).</summary>
    Task<IReadOnlyList<ApprovalAction>> GetHistoryAsync(
        ApprovalDocumentType documentType, Guid documentId, CancellationToken cancellationToken = default);
}
