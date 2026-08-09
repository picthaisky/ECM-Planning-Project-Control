using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>S9-BE-05: <see cref="IPaymentCertificateRepository"/> against <see cref="CmPlusDbContext"/>.</summary>
public sealed class PaymentCertificateRepository(CmPlusDbContext dbContext) : IPaymentCertificateRepository
{
    /// <summary>
    /// Tracked, and critically <c>Include</c>s <see cref="PaymentCertificate.ApprovalSteps"/>
    /// (security review sprint-09.md H-01 fix): Approve/Reject/ReturnForRevision now resolve step
    /// authority entirely from that snapshot rather than re-querying <c>ApprovalPolicyRule</c>, and
    /// <c>ReturnForRevision</c>/<c>Withdraw</c>'s <c>VoidChainSnapshot</c> clears the in-memory
    /// collection expecting EF's change tracker to detect and cascade-delete the removed rows on the
    /// next <c>SaveChanges</c> - both require the collection to be loaded up front, not lazily.
    /// </summary>
    public Task<PaymentCertificate?> FindAsync(Guid paymentCertificateId, CancellationToken cancellationToken = default) =>
        dbContext.PaymentCertificates
            .Include(c => c.ApprovalSteps)
            .FirstOrDefaultAsync(c => c.Id == paymentCertificateId, cancellationToken);

    public void AddLedgerEntries(IReadOnlyList<ProjectFinanceLedger> entries) =>
        dbContext.ProjectFinanceLedgers.AddRange(entries);

    public async Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // PaymentCertificate.RowVersion (IsRowVersion(), PaymentCertificateConfiguration) is the
            // optimistic-concurrency token - two simultaneous approvers both loading the same row
            // race here, and the loser's UPDATE affects zero rows. Translating that into a plain
            // `false` (rather than letting the EF-specific exception escape this Infrastructure
            // project) is what keeps CMPlus.Application free of any Microsoft.EntityFrameworkCore
            // reference (ADR-0001) while still letting the command handler map it to a 409
            // ProblemDetails (design.md §2.3 "concurrent-transition").
            return false;
        }
    }
}
