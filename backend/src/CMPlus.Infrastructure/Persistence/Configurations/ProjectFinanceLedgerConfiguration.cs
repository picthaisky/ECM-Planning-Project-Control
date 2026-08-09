using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for <see cref="ProjectFinanceLedger"/> (S9-BE-04, payment-retention.md §4,
/// docs/10 §3/S9-DB-01). <c>ProjectId</c> is deliberately not modelled as an EF-level FK
/// relationship, matching this codebase's existing convention for that column
/// (<c>ActualCostEntry</c>/<c>WBSNode</c> reference <c>ProjectId</c> the same way).
/// </summary>
public sealed class ProjectFinanceLedgerConfiguration : IEntityTypeConfiguration<ProjectFinanceLedger>
{
    public void Configure(EntityTypeBuilder<ProjectFinanceLedger> builder)
    {
        builder.ToTable("ProjectFinanceLedgers");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.Property(e => e.Reference).HasMaxLength(64);
        builder.Property(e => e.Note).HasMaxLength(500);

        // A certificate with ledger rows posted against it cannot be deleted. Restrict, not cascade
        // - this codebase's default everywhere else (ActualCostEntry -> WBSNode/Activity).
        builder.HasOne<PaymentCertificate>()
            .WithMany()
            .HasForeignKey(e => e.PaymentCertificateId)
            .OnDelete(DeleteBehavior.Restrict);

        // design.md §3 mandatory index: "ProjectFinanceLedger (TenantId, ProjectId, Category) - the
        // SUM() of retention/advance must seek" (S9-DB-01 DoD).
        builder.HasIndex(e => new { e.TenantId, e.ProjectId, e.Category });

        builder.ToTable(tb =>
        {
            tb.HasCheckConstraint("CK_ProjectFinanceLedgers_Amount_NotZero", "[Amount] <> 0");
        });

        // Append-only (payment-retention.md §4): no update/delete path exists on the entity (no
        // mutator methods/setters - verified structurally by CMPlus.Domain.Tests) or here in
        // configuration.
    }
}
