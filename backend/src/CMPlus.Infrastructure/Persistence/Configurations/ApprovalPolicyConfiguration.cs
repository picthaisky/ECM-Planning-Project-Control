using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

public sealed class ApprovalPolicyConfiguration : IEntityTypeConfiguration<ApprovalPolicy>
{
    public void Configure(EntityTypeBuilder<ApprovalPolicy> builder)
    {
        builder.ToTable("ApprovalPolicies");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.CumulativeVoEscalationPct).HasPrecision(5, 2);

        // Owned collection navigation backed by the private `_rules` field - ApprovalPolicyRule
        // has no public constructor/adder, so EF must populate it via the backing field, never
        // through the (nonexistent) public setter (design.md §1.2: rule bands live on the policy
        // aggregate, not a top-level DbSet callers mutate directly).
        builder.HasMany(p => p.Rules)
            .WithOne()
            .HasForeignKey(r => r.ApprovalPolicyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(ApprovalPolicy.Rules))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // design.md §3 data model note: only one active policy per (TenantId, ProjectId,
        // DocumentType) - two active policies for one scope is a data-integrity bug.
        builder.HasIndex(p => new { p.TenantId, p.ProjectId, p.DocumentType })
            .IsUnique()
            .HasFilter("[IsActive] = 1");

        builder.ToTable(tb => tb.HasCheckConstraint(
            "CK_ApprovalPolicies_CumulativeVoEscalationPct", "[CumulativeVoEscalationPct] IS NULL OR [CumulativeVoEscalationPct] BETWEEN 0 AND 100"));

        // Version-pinned/immutable: no update path is exposed anywhere except Deactivate()
        // (IsActive/EffectiveTo only) - editing rules always creates a brand-new row via
        // CreateNextVersion, never an in-place edit (approval-workflow.md §5.2).
    }
}
