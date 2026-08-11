using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(250).IsRequired();
        builder.Property(p => p.Code).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Owner).HasMaxLength(250).IsRequired();

        // Money: decimal(18,2). Percent: decimal(5,2). PF (stored): decimal(9,4) - all per
        // docs/db-conventions.md §3 / ADR-0007.
        builder.Property(p => p.BAC).HasPrecision(18, 2);
        builder.Property(p => p.ContractValue).HasPrecision(18, 2);
        // ADR-0015 / sprint-10 security review H-02: the cumulative-VO-escalation baseline - NOT NULL
        // (backfilled by migration; see Project.cs's own remarks on OriginalContractValue for why a
        // nullable-with-a-fallback shape was a live defect, not a safe default).
        builder.Property(p => p.OriginalContractValue).HasPrecision(18, 2);
        // EscalationBaselineContractValue is a computed, get-only convenience property
        // (a passthrough to OriginalContractValue) with no backing field - explicitly excluded rather
        // than relying on EF Core's convention-based discovery to skip it correctly.
        builder.Ignore(p => p.EscalationBaselineContractValue);
        // domain-rules.md §5.5(c): BAC^orig, the BAC(t) reconstruction origin - same NOT NULL,
        // backfilled-by-migration shape as OriginalContractValue immediately above (H-02).
        builder.Property(p => p.OriginalBac).HasPrecision(18, 2);
        // EffectiveOriginalBac is a computed, get-only convenience property (a passthrough to
        // OriginalBac) with no backing field - same reason EscalationBaselineContractValue is ignored
        // above.
        builder.Ignore(p => p.EffectiveOriginalBac);
        // Sprint-10 security review H-03: optimistic-concurrency token - see Project.RowVersion's own
        // remarks. Same IsRowVersion() shape as VariationOrderConfiguration/PaymentCertificateConfiguration.
        builder.Property(p => p.RowVersion).IsRowVersion();
        builder.Property(p => p.RetentionRate).HasPrecision(5, 2);
        builder.Property(p => p.AdvanceRate).HasPrecision(5, 2);
        builder.Property(p => p.RetentionCapPercentage).HasPrecision(5, 2);
        builder.Property(p => p.RetentionRelease1Percentage).HasPrecision(5, 2);
        builder.Property(p => p.AdvanceAmountPaid).HasPrecision(18, 2);
        builder.Property(p => p.AdvanceRecoveryStartPct).HasPrecision(5, 2);
        builder.Property(p => p.AdvanceRecoveryRatePct).HasPrecision(5, 2);
        builder.Property(p => p.AdvanceRecoveryEndPct).HasPrecision(5, 2);
        builder.Property(p => p.EacCustomPerformanceFactor).HasPrecision(9, 4);
        builder.Property(p => p.EacManualEtc).HasPrecision(18, 2);

        builder.HasIndex(p => new { p.TenantId, p.Code }).IsUnique();

        // CHECK constraints: DB-layer defense-in-depth mirroring Domain invariants
        // (docs/db-conventions.md §3.1) - protects against direct-SQL fixes/bad backfills that
        // bypass the Domain layer. NULL is deliberately allowed through (see Project.cs remarks:
        // null != 0 for RetentionRate/AdvanceRate) - SQL Server's three-valued logic already
        // treats a NULL operand as satisfying the CHECK, so no explicit "IS NULL OR" is needed.
        builder.ToTable(tb =>
        {
            tb.HasCheckConstraint("CK_Projects_BAC", "[BAC] >= 0");
            tb.HasCheckConstraint("CK_Projects_ContractValue", "[ContractValue] >= 0");
            tb.HasCheckConstraint("CK_Projects_OriginalContractValue", "[OriginalContractValue] >= 0");
            tb.HasCheckConstraint("CK_Projects_OriginalBac", "[OriginalBac] >= 0");
            tb.HasCheckConstraint(
                "CK_Projects_ApprovedVariationOrderCount", "[ApprovedVariationOrderCount] >= 0");
            tb.HasCheckConstraint("CK_Projects_RetentionRate", "[RetentionRate] BETWEEN 0 AND 100");
            tb.HasCheckConstraint("CK_Projects_AdvanceRate", "[AdvanceRate] BETWEEN 0 AND 100");
            tb.HasCheckConstraint("CK_Projects_RetentionCapPercentage", "[RetentionCapPercentage] BETWEEN 0 AND 100");
            tb.HasCheckConstraint("CK_Projects_RetentionRelease1Percentage", "[RetentionRelease1Percentage] BETWEEN 0 AND 100");
            tb.HasCheckConstraint("CK_Projects_AdvanceAmountPaid", "[AdvanceAmountPaid] >= 0");
            tb.HasCheckConstraint("CK_Projects_AdvanceRecoveryStartPct", "[AdvanceRecoveryStartPct] BETWEEN 0 AND 100");
            tb.HasCheckConstraint("CK_Projects_AdvanceRecoveryRatePct", "[AdvanceRecoveryRatePct] BETWEEN 0 AND 100");
            tb.HasCheckConstraint("CK_Projects_AdvanceRecoveryEndPct", "[AdvanceRecoveryEndPct] BETWEEN 0 AND 100");
            tb.HasCheckConstraint("CK_Projects_EacCustomPerformanceFactor", "[EacCustomPerformanceFactor] > 0");
            tb.HasCheckConstraint("CK_Projects_EacManualEtc", "[EacManualEtc] >= 0");
        });
    }
}
