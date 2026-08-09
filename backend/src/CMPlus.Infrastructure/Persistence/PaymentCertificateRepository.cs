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

    /// <summary>
    /// Read-only sibling of <see cref="FindAsync"/> - see the interface's own remarks. "Newest
    /// first" orders by <see cref="PaymentCertificate.Id"/> (a UUIDv7, time-ordered by construction,
    /// <c>Entity.cs</c>), sorted client-side (after <c>ToListAsync</c>, never as a SQL
    /// <c>ORDER BY</c>) by the canonical hex string (<see cref="Guid.ToString()"/>, ordinal
    /// comparison) rather than a bare <c>Guid</c> comparison.
    ///
    /// <para><b>Two distinct, independently-verified caveats, not one:</b></para>
    /// <para>(1) SQL Server's own <c>uniqueidentifier</c> collation is a real, long-documented byte
    /// reordering unrelated to RFC 9562's intended left-to-right comparison - this cannot be
    /// verified here (no SQL Server available, Docker outage), so sorting is deliberately done
    /// entirely in .NET, after materialization, rather than trusted to a pushed-down
    /// <c>OrderByDescending(c =&gt; c.Id)</c> that a provider might translate using its own collation.
    /// Ordinal comparison of the same-length canonical hex string reproduces RFC 9562's intended
    /// byte-order comparison exactly (two hex characters map 1:1 to one byte, compared strictly left
    /// to right) regardless of provider.</para>
    /// <para>(2) <b>More significant, and empirically confirmed</b> (throwaway probe, this task):
    /// <see cref="Guid.CreateVersion7"/> has only millisecond resolution and no monotonic counter for
    /// two ids minted within the same millisecond - its sub-millisecond bits are plain random per RFC
    /// 9562. Two certificates created back-to-back with no delay disagree with creation order
    /// roughly 80% of the time in that probe, and - importantly - <i>no comparison strategy fixes
    /// this</i>, including the ordinal-string one here: both it and a bare <c>Guid</c> comparison
    /// failed at an identical rate once the compared values shared a millisecond, because the values
    /// themselves carry no reliable sub-millisecond order to recover. A genuine fix needs a
    /// dedicated <c>CreatedAt</c> column, deliberately not added by this read-side gap closure: there
    /// is today no production path that creates a <see cref="PaymentCertificate"/> at all
    /// (`grep "new PaymentCertificate(" backend/src` returns zero hits), so two certificates cannot
    /// yet race within the same millisecond in production either - this is a real, documented
    /// limitation for whoever builds the create-certificate command to weigh (see this task's
    /// handoff report), not a live bug today.</para>
    ///
    /// <para>The per-project row count is small (milestone periods, not WBS-tree-scale volume), so
    /// sorting client-side is not the kind of "sort in memory" that risks loading an unbounded result
    /// set.</para>
    /// </summary>
    public async Task<IReadOnlyList<PaymentCertificate>> ListByProjectAsync(
        Guid projectId, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.PaymentCertificates
            .AsNoTracking()
            .Include(c => c.ApprovalSteps)
            .Where(c => c.ProjectId == projectId)
            .ToListAsync(cancellationToken);

        return rows
            .OrderByDescending(c => c.Id.ToString(), StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Read-only sibling of <see cref="FindAsync"/> - see the interface's own remarks.</summary>
    public Task<PaymentCertificate?> GetByIdAsync(Guid paymentCertificateId, CancellationToken cancellationToken = default) =>
        dbContext.PaymentCertificates
            .AsNoTracking()
            .Include(c => c.ApprovalSteps)
            .FirstOrDefaultAsync(c => c.Id == paymentCertificateId, cancellationToken);

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
