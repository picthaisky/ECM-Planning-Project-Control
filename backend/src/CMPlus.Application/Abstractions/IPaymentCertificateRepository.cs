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

    /// <summary>
    /// Every certificate for one project, newest-first (S9 read-side gap closure, finding L-04;
    /// docs/9 §6: <c>GET /api/v1/projects/{id}/payment-certificates</c>) - the milestone/certificate
    /// table the Payment Certificate screen needs
    /// (<c>web/src/features/payment/api.ts#listPaymentCertificates</c>, already built and waiting on
    /// this endpoint). "Newest" orders by <see cref="CMPlus.Domain.Common.Entity.Id"/> descending -
    /// a UUIDv7, so creation-time-ordered by construction (<c>Entity.cs</c>) - there is no separate
    /// <c>CreatedAt</c> column on <see cref="PaymentCertificate"/> to sort by instead. Only reliable
    /// at millisecond granularity - see <c>PaymentCertificateRepository.ListByProjectAsync</c>'s
    /// remarks for the empirically-verified reason (no monotonic counter for two ids minted in the
    /// same millisecond) and why that is not fixed here.
    ///
    /// <para><see langword="AsNoTracking"/> + <c>Include(ApprovalSteps)</c>, mirroring
    /// <c>IEvmSnapshotRepository.ListAsync</c>'s "repository read returns entities, DTO mapping
    /// happens in the query handler" shape - <see cref="PaymentCertificateDto.From"/> is reused
    /// as-is, never duplicated as a second projection. Tenant-scoped by the global EF query filter
    /// (ADR-0002) and additionally scoped by <paramref name="projectId"/> here - a project-scoped
    /// route, unlike this aggregate's flat <c>/payment-certificates/{id}/...</c> transition routes
    /// (security review sprint-09.md M-02: scoping the list by project is "strictly better than the
    /// flat-route situation the review criticised", though M-02 itself - same-tenant-wrong-project
    /// authorization on the id-scoped actions - remains open and is not what this closes).</para>
    /// </summary>
    Task<IReadOnlyList<PaymentCertificate>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Single-certificate read (S9 read-side gap closure, finding L-04:
    /// <c>GET /api/v1/payment-certificates/{id}</c>) - <see langword="AsNoTracking"/> +
    /// <c>Include(ApprovalSteps)</c>, the read-only sibling of <see cref="FindAsync"/> for callers
    /// that will never follow this load with <see cref="TrySaveChangesAsync"/>. Tenant-scoped by the
    /// global EF query filter (ADR-0002) - a cross-tenant id is indistinguishable from "does not
    /// exist", the same guarantee <see cref="FindAsync"/> already gives the five mutating commands.
    /// </summary>
    Task<PaymentCertificate?> GetByIdAsync(Guid paymentCertificateId, CancellationToken cancellationToken = default);
}
