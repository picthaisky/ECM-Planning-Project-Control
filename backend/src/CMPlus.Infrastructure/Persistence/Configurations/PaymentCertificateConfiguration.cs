using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for <see cref="PaymentCertificate"/> (S9-BE-01, docs/10 §3/S9-DB-01/02).
/// <c>ProjectId</c> is deliberately not modelled as an EF-level FK relationship, matching this
/// codebase's existing convention for that column (<c>ActualCostEntry</c>/<c>WBSNode</c>/
/// <c>EvmPeriodSnapshot</c> all reference <c>ProjectId</c> the same way) - project existence is
/// checked at the Application/repository layer instead.
/// </summary>
public sealed class PaymentCertificateConfiguration : IEntityTypeConfiguration<PaymentCertificate>
{
    public void Configure(EntityTypeBuilder<PaymentCertificate> builder)
    {
        builder.ToTable("PaymentCertificates");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(500);
        builder.Property(c => c.PaymentReference).HasMaxLength(64);

        builder.Property(c => c.MilestoneValue).HasPrecision(18, 2);
        builder.Property(c => c.PreviousCumulativeApprovePct).HasPrecision(5, 2);
        builder.Property(c => c.ApprovePct).HasPrecision(5, 2);
        builder.Property(c => c.ClaimPct).HasPrecision(5, 2);
        builder.Property(c => c.ActualProgressPct).HasPrecision(5, 2);
        builder.Property(c => c.GrossCertifiedAmount).HasPrecision(18, 2);
        builder.Property(c => c.RetentionAmount).HasPrecision(18, 2);
        builder.Property(c => c.AdvanceRecoveryAmount).HasPrecision(18, 2);
        builder.Property(c => c.NetPayment).HasPrecision(18, 2);

        // design.md §3: "RowVersion (rowversion) on PaymentCertificate ... for optimistic
        // concurrency" - two simultaneous approvers produce a DbUpdateConcurrencyException on the
        // second SaveChanges (S9-BE-01 DoD: "409, never a double-advance").
        builder.Property(c => c.RowVersion).IsRowVersion();

        // Security review sprint-09.md H-01 fix: owned collection navigation backed by the private
        // `_approvalSteps` field - PaymentCertificateApprovalStep has no public constructor/adder, so
        // EF must populate it via the backing field, never through a (nonexistent) public setter.
        // Same pattern as ApprovalPolicyConfiguration's own Rules navigation. Cascade delete: a
        // certificate's step snapshot has no reason to outlive the certificate itself.
        builder.HasMany(c => c.ApprovalSteps)
            .WithOne()
            .HasForeignKey(s => s.PaymentCertificateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(PaymentCertificate.ApprovalSteps))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Certificate-list-by-project/status is the natural read pattern for the (not-yet-built)
        // Payment Certificate screen and for resolving "the prior certificate for this milestone".
        builder.HasIndex(c => new { c.TenantId, c.ProjectId, c.Status });
        builder.HasIndex(c => new { c.TenantId, c.ProjectId, c.MilestoneNo });

        // CHECK constraints: DB-layer defense-in-depth mirroring the Domain invariants
        // (docs/db-conventions.md §3.1, S9-DB-02 DoD). Both are already guaranteed by
        // PaymentCertificate.SetPeriodClaim (PercentageGuard/MoneyGuard) - these never actually
        // fire in practice, same discipline as EvmPeriodSnapshot's own CHECK constraints.
        builder.ToTable(tb =>
        {
            tb.HasCheckConstraint("CK_PaymentCertificates_ApprovePct", "[ApprovePct] >= 0 AND [ApprovePct] <= 100");
            tb.HasCheckConstraint("CK_PaymentCertificates_GrossCertifiedAmount", "[GrossCertifiedAmount] >= 0");
        });
    }
}
