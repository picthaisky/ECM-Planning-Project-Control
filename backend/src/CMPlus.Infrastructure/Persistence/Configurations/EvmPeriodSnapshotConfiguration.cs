using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

public sealed class EvmPeriodSnapshotConfiguration : IEntityTypeConfiguration<EvmPeriodSnapshot>
{
    public void Configure(EntityTypeBuilder<EvmPeriodSnapshot> builder)
    {
        builder.ToTable("EvmPeriodSnapshots");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.TenantId).IsRequired();

        // Money decimal(18,2); PF decimal(9,6) "to keep the audit reproducible" (design.md §3) -
        // distinct from Project.EacCustomPerformanceFactor's own decimal(9,4) input precision.
        builder.Property(s => s.Bac).HasPrecision(18, 2);
        builder.Property(s => s.Pv).HasPrecision(18, 2);
        builder.Property(s => s.Ev).HasPrecision(18, 2);
        builder.Property(s => s.Ac).HasPrecision(18, 2);
        builder.Property(s => s.PerformanceFactor).HasPrecision(9, 6);
        builder.Property(s => s.Eac).HasPrecision(18, 2);
        builder.Property(s => s.Etc).HasPrecision(18, 2);
        builder.Property(s => s.Vac).HasPrecision(18, 2);

        // S7-DB-01 mandatory unique index (design.md §3): "closing the same dataDate twice is a
        // data-integrity bug, not a runtime tie-break" - CloseEvmPeriodCommandHandler's app-level
        // ExistsForDataDateAsync check is defense in depth alongside this, not a substitute for it.
        builder.HasIndex(s => new { s.TenantId, s.ProjectId, s.DataDate }).IsUnique();

        // CHECK constraints: DB-layer defense-in-depth mirroring the Domain invariants
        // (docs/db-conventions.md §3.1). Ac/Eac/Etc/Vac deliberately have none - Eac/Etc/Vac may
        // legitimately be negative (a forecast overrun, or EV > BAC's negative ETC), and Ac's own
        // non-negative CHECK was dropped alongside the Domain-layer fix (ActualCostEntry work,
        // ADR-0013's consequences): once a real ActualCostEntry ledger exists, a credit note or
        // over-reversal can legitimately produce a negative AC(t) (actual-cost.md §6.5/§7.6), and
        // this CHECK would otherwise reject closing a period at that (correct, if unusual) value.
        builder.ToTable(tb =>
        {
            tb.HasCheckConstraint("CK_EvmPeriodSnapshots_Bac", "[Bac] >= 0");
            tb.HasCheckConstraint("CK_EvmPeriodSnapshots_Pv", "[Pv] >= 0");
            tb.HasCheckConstraint("CK_EvmPeriodSnapshots_Ev", "[Ev] >= 0");
        });

        // Append-only (S7-BE-05 DoD: "immutable - no update path at all"): no update/delete path
        // exists on the entity (no mutator methods/setters - verified structurally by
        // CMPlus.Domain.Tests) or here in configuration.
    }
}
