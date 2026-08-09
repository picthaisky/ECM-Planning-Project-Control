using CMPlus.Domain.Entities;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// Persistence boundary for the <see cref="PaymentCertificate"/> approval-workflow commands
/// (S9-BE-05: Submit/Approve/ReturnForRevision/Reject/RecordPayment). Mirrors
/// <c>IProjectRepository</c>'s "tracked load, mutate via domain methods, explicit
/// <see cref="TrySaveChangesAsync"/>" shape - the one deliberate difference is
/// <see cref="AddLedgerEntries"/>, because <see cref="PaymentCertificate.CreateCertificationLedgerEntries"/>
/// (S9-BE-04) is a same-transaction side effect of <c>Approve</c> reaching <c>Certified</c>, not an
/// unrelated aggregate a rate-change-style command must be structurally prevented from reaching.
/// </summary>
public interface IPaymentCertificateRepository
{
    /// <summary>Tracked (not <c>AsNoTracking</c>) - the caller mutates the returned instance via its
    /// domain methods and this same instance is what <see cref="TrySaveChangesAsync"/> persists.
    /// Tenant-scoped by the global EF query filter (ADR-0002) - a certificate id belonging to
    /// another tenant is indistinguishable from "does not exist" (closes the IDOR concern
    /// S9-SEC-01 must verify).</summary>
    Task<PaymentCertificate?> FindAsync(Guid paymentCertificateId, CancellationToken cancellationToken = default);

    /// <summary>Stages the ledger rows a certificate reaching <c>Certified</c> posts
    /// (<see cref="PaymentCertificate.CreateCertificationLedgerEntries"/>, S9-BE-04) - persisted in
    /// the same <see cref="TrySaveChangesAsync"/> call as the certificate's own state change, so the
    /// two can never diverge (a certificate that is <c>Certified</c> with no matching ledger rows,
    /// or vice versa, would be a data-integrity bug).</summary>
    void AddLedgerEntries(IReadOnlyList<ProjectFinanceLedger> entries);

    /// <summary>
    /// Persists every staged change for this unit of work (the certificate's own mutation, any
    /// <see cref="AddLedgerEntries"/> rows, and - via <see cref="IApprovalActionRepository.Add"/>
    /// sharing the same scoped <c>DbContext</c> - the new <see cref="ApprovalAction"/> row) as one
    /// atomic transaction. Returns <see langword="false"/> instead of throwing when a concurrent
    /// writer already changed this exact <see cref="PaymentCertificate"/> row between this
    /// request's <see cref="FindAsync"/> and this call (<see cref="PaymentCertificate.RowVersion"/>
    /// mismatch) - design.md §2.3's <c>409 concurrent-transition</c>, "two simultaneous approvers,
    /// the second gets 409, never a double-advance" (S9-BE-01 DoD, now wired up for real).
    /// </summary>
    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default);
}
