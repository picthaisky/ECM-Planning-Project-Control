using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for <see cref="ActualCostEntry"/> - actual-cost.md §9 (ADR-0013). All four
/// indexes lead with <c>TenantId</c> (docs/db-conventions.md §2). <c>ProjectId</c> is deliberately
/// not modelled as an EF-level FK relationship, matching this codebase's existing convention for
/// that column (<c>WBSNode</c>/<c>EvmPeriodSnapshot</c>/<c>FileImportJob</c> all reference
/// <c>ProjectId</c> the same way) - project existence is checked at the Application/repository
/// layer instead.
/// </summary>
public sealed class ActualCostEntryConfiguration : IEntityTypeConfiguration<ActualCostEntry>
{
    public void Configure(EntityTypeBuilder<ActualCostEntry> builder)
    {
        builder.ToTable("ActualCostEntries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.Property(e => e.DocumentReference).HasMaxLength(64);
        builder.Property(e => e.CostCode).HasMaxLength(32);
        builder.Property(e => e.VendorName).HasMaxLength(200);
        builder.Property(e => e.Note).HasMaxLength(500);
        builder.Property(e => e.UnitOfMeasure).HasMaxLength(16);

        // Referential integrity (actual-cost.md §10.8): a WBSNode/Activity with AC posted against
        // it cannot be deleted. Restrict, not cascade - same default this codebase uses everywhere
        // else (WBSNode's own self-referencing ParentWbsNodeId, ActivityProgressLog -> Activity).
        builder.HasOne<WBSNode>()
            .WithMany()
            .HasForeignKey(e => e.WbsNodeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Activity>()
            .WithMany()
            .HasForeignKey(e => e.ActivityId)
            .OnDelete(DeleteBehavior.Restrict);

        // Self-referencing FK: accrual reversal / correction pairing (actual-cost.md §6.5).
        builder.HasOne<ActualCostEntry>()
            .WithMany()
            .HasForeignKey(e => e.ReversesEntryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FileImportJob>()
            .WithMany()
            .HasForeignKey(e => e.FileImportJobId)
            .OnDelete(DeleteBehavior.Restrict);

        // actual-cost.md §9's four mandatory indexes.
        // (1) The AC(t) seek - primary.
        builder.HasIndex(e => new { e.TenantId, e.ProjectId, e.IncurredDate })
            .IncludeProperties(e => new { e.Amount });

        // (2) Control-account (node) CPI.
        builder.HasIndex(e => new { e.TenantId, e.WbsNodeId, e.IncurredDate })
            .IncludeProperties(e => new { e.Amount });

        // (3) Activity CPI - filtered because most rows have a NULL ActivityId.
        builder.HasIndex(e => new { e.TenantId, e.ActivityId, e.IncurredDate })
            .HasFilter("[ActivityId] IS NOT NULL");

        // (4) Category-breakdown chart.
        builder.HasIndex(e => new { e.TenantId, e.ProjectId, e.CostCategory, e.IncurredDate })
            .IncludeProperties(e => new { e.Amount });

        // CHECK constraints: DB-layer defense-in-depth mirroring the Domain invariants
        // (docs/db-conventions.md §3.1). Amount deliberately has no non-negative CHECK - it is
        // signed, and a reversal/adjustment/credit note is legitimately negative (actual-cost.md
        // §6.5/§7.6) - but it is never exactly zero (§7.6: a zero entry, including a zero reversal,
        // is noise). Quantity mirrors ActivityProgressLog.ActualQuantity's own nullable >= 0 CHECK.
        builder.ToTable(tb =>
        {
            tb.HasCheckConstraint("CK_ActualCostEntries_Amount_NotZero", "[Amount] <> 0");
            tb.HasCheckConstraint("CK_ActualCostEntries_Quantity", "[Quantity] IS NULL OR [Quantity] >= 0");
        });

        // Append-only (actual-cost.md §6.5/§10.1): no update/delete path exists on the entity (no
        // mutator methods/setters - verified structurally by CMPlus.Domain.Tests) or here in
        // configuration.
    }
}
