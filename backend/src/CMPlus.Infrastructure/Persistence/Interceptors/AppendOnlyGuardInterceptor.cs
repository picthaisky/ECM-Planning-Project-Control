using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CMPlus.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Structural enforcement of "append-only" (security review sprint-09.md M-01): throws when
/// <c>SavingChanges(Async)</c> detects any <see cref="IAppendOnly"/> entity in the
/// <see cref="EntityState.Modified"/> or <see cref="EntityState.Deleted"/> state, so the guarantee
/// does not rest on developer discipline alone. Execution-verified reachable through an ordinary
/// <see cref="CmPlusDbContext"/> before this interceptor existed (review probe 7: an
/// <c>ApprovalAction</c> row was rewritten and deleted with no error at all).
///
/// <para>Applies uniformly via the <see cref="IAppendOnly"/> marker rather than a per-entity-type
/// list, so a future append-only entity is covered by implementing the marker, not by remembering to
/// add a new special case here. <see cref="EntityState.Added"/> is deliberately unaffected -
/// appending is the one operation this guarantee exists to still allow.</para>
///
/// <para><b>M-01 money-freeze half (security review sprint-09.md §9.4/§9.5):</b> also guards
/// <see cref="PaymentCertificate"/>'s money fields specifically, since that aggregate is neither
/// <see cref="IAppendOnly"/> (its Status/StepNo fields must keep advancing through the workflow) nor
/// <see cref="INeverModified"/> (same reason). Before this guard, "money fields are frozen once a
/// certificate leaves Draft" held only as a Domain-method invariant
/// (<c>PaymentCertificate.EnsureMoneyFieldsEditable</c>, guarding <c>SetPeriodClaim</c>) - an ordinary
/// <c>CmPlusDbContext</c> could still rewrite <c>NetPayment</c> on a <c>Certified</c> certificate via
/// <c>context.Entry(...).Property(...).CurrentValue = ...</c> with no error at all
/// (execution-verified, security review re-confirmation this session). This check is scoped to the
/// five money properties named on <c>SetPeriodClaim</c>'s own doc comment and fires only when one of
/// them is actually <see cref="PropertyEntry.IsModified"/> - so it never blocks a legitimate
/// status/step transition (Submit/Approve/Reject/ReturnForRevision/RecordPayment) that happens to run
/// while <see cref="PaymentCertificate.Status"/> is no longer <c>NotDue</c>/<c>Draft</c>, only an
/// actual attempt to change money on such a row.</para>
/// </summary>
public sealed class AppendOnlyGuardInterceptor : SaveChangesInterceptor
{
    /// <summary>The exact five properties <c>PaymentCertificate.SetPeriodClaim</c>'s own doc comment
    /// names as the money fields it - and only it - may change. <c>ClaimPct</c>/<c>ActualProgressPct</c>
    /// are reporting-only (see their own remarks on the entity) and deliberately out of scope here.</summary>
    private static readonly string[] PaymentCertificateFrozenMoneyProperties =
    [
        nameof(PaymentCertificate.ApprovePct),
        nameof(PaymentCertificate.GrossCertifiedAmount),
        nameof(PaymentCertificate.RetentionAmount),
        nameof(PaymentCertificate.AdvanceRecoveryAmount),
        nameof(PaymentCertificate.NetPayment),
    ];

    /// <summary>domain-rules.md §2.4's freeze matrix: the exact set <c>VariationOrder.SetVariationContent</c>
    /// is the sole writer of - frozen once <c>Status</c> leaves <c>Draft</c>. Mirrors
    /// <see cref="PaymentCertificateFrozenMoneyProperties"/>'s M-01 pattern for the second aggregate
    /// that shares this shape (domain-rules.md §2.4: "The money/scope freeze must be enforced at the
    /// persistence layer too ... not only by the aggregate's discipline"). <c>Type</c> is excluded - it
    /// is a computed property with no backing column at all (see the entity's own remarks), so there is
    /// nothing for a change tracker to ever mark <c>IsModified</c>.</summary>
    private static readonly string[] VariationOrderFrozenContentProperties =
    [
        nameof(VariationOrder.Amount),
        nameof(VariationOrder.Description),
        nameof(VariationOrder.Justification),
        nameof(VariationOrder.TimeImpactDays),
    ];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AssertNoIllegalMutation(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        AssertNoIllegalMutation(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void AssertNoIllegalMutation(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is IAppendOnly && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{entry.Entity.GetType().Name} ('{entry.Property(nameof(CMPlus.Domain.Common.Entity.Id)).CurrentValue}') " +
                    $"is append-only and cannot be {entry.State}. Corrections must be a new compensating row, " +
                    "never an edit or delete (conventions.md).");
            }

            // Narrower guarantee (S9-SEC-02 finding N-01): rows that may be added and cleared as a
            // set, but never edited individually. `PaymentCertificateApprovalStep` is the case -
            // ReturnForRevision/Withdraw legitimately clear the whole snapshot so a resubmission can
            // re-resolve a fresh chain, but since the H-01 fix that snapshot is the *sole* record of
            // who may approve which step, so editing one rung is a direct authority escalation.
            if (entry.Entity is INeverModified && entry.State is EntityState.Modified)
            {
                throw new InvalidOperationException(
                    $"{entry.Entity.GetType().Name} ('{entry.Property(nameof(CMPlus.Domain.Common.Entity.Id)).CurrentValue}') " +
                    "cannot be modified in place. It may only be added, or removed as part of clearing " +
                    "the whole set (e.g. ReturnForRevision voiding a chain snapshot).");
            }

            if (entry.Entity is PaymentCertificate && entry.State is EntityState.Modified)
            {
                AssertNoFrozenMoneyFieldMutation(entry);
            }

            if (entry.Entity is VariationOrder && entry.State is EntityState.Modified)
            {
                AssertNoFrozenVariationOrderContentMutation(entry);
            }
        }
    }

    /// <summary>M-01 money-freeze half. Mirrors <c>PaymentCertificate.EnsureMoneyFieldsEditable</c>'s
    /// own predicate exactly (frozen unless <c>NotDue</c>/<c>Draft</c>) rather than a numeric
    /// <c>&gt;= PendingApproval</c> comparison, so the two never drift if the enum's members are ever
    /// reordered or extended.</summary>
    private static void AssertNoFrozenMoneyFieldMutation(EntityEntry entry)
    {
        var status = (PaymentCertificateStatus)entry.Property(nameof(PaymentCertificate.Status)).CurrentValue!;
        if (status is PaymentCertificateStatus.NotDue or PaymentCertificateStatus.Draft)
        {
            return;
        }

        foreach (var propertyName in PaymentCertificateFrozenMoneyProperties)
        {
            if (entry.Property(propertyName).IsModified)
            {
                throw new InvalidOperationException(
                    $"PaymentCertificate ('{entry.Property(nameof(CMPlus.Domain.Common.Entity.Id)).CurrentValue}') money field " +
                    $"'{propertyName}' is frozen once Status leaves NotDue/Draft (current status: {status}) and cannot be " +
                    "modified. Corrections after Certified are a new certificate, never an edit (conventions.md, M-01).");
            }
        }
    }

    /// <summary>M-01 pattern applied to the second aggregate that shares this shape. Mirrors
    /// <see cref="AssertNoFrozenMoneyFieldMutation"/>'s predicate exactly (frozen unless <c>Draft</c>),
    /// keyed off <c>VariationOrder.EnsureContentEditable</c>'s own boundary rather than a numeric
    /// comparison, so the two can never silently drift if <c>VariationOrderStatus</c>'s members are
    /// ever reordered or extended.</summary>
    private static void AssertNoFrozenVariationOrderContentMutation(EntityEntry entry)
    {
        var status = (VariationOrderStatus)entry.Property(nameof(VariationOrder.Status)).CurrentValue!;
        if (status == VariationOrderStatus.Draft)
        {
            return;
        }

        foreach (var propertyName in VariationOrderFrozenContentProperties)
        {
            if (entry.Property(propertyName).IsModified)
            {
                throw new InvalidOperationException(
                    $"VariationOrder ('{entry.Property(nameof(CMPlus.Domain.Common.Entity.Id)).CurrentValue}') content field " +
                    $"'{propertyName}' is frozen once Status leaves Draft (current status: {status}) and cannot be " +
                    "modified. A returned-for-revision document (back in Draft) may be re-priced; nothing else may " +
                    "(domain-rules.md §2.4, M-01 pattern).");
            }
        }
    }
}
