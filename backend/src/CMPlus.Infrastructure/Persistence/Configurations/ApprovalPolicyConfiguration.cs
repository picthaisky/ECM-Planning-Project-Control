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
        //
        // ADR-0021: this used to be a single filtered unique index
        // `(TenantId, ProjectId, DocumentType) WHERE IsActive = 1`. Standard ANSI SQL treats
        // NULL <> NULL for uniqueness purposes, so that single index provided ZERO protection for
        // the ProjectId IS NULL group - and every policy UpdateApprovalPolicyCommandHandler can
        // actually create today has ProjectId = null (tenant-wide default; project-scoped override
        // is schema-present but unexposed - see ApprovalPolicy.ProjectId's own remarks). Two
        // concurrent PUT .../approval-policies/{documentType} requests could both insert an
        // IsActive=1, ProjectId=null row and both succeed, leaving two simultaneously-active
        // versions permanently (proven in
        // ApprovalPolicyActivationConcurrencySqliteTests.Tenant_Wide_Default_Policy_ProjectId_Is_Null_...).
        //
        // Fixed by splitting into two filtered indexes on disjoint ProjectId-nullability groups, so
        // every index's key columns are non-null wherever its filter applies and uniqueness
        // actually bites. Column shape is derived directly from the real query predicates, not just
        // the ADR sentence:
        //   - ApprovalPolicyReader.GetActiveTenantDefaultPolicyAsync and
        //     ApprovalPolicyRepository.FindActiveTenantDefaultAsync both filter exactly
        //     (TenantId [ambient], DocumentType, ProjectId IS NULL, IsActive) - this index is a
        //     full seek for both, and is the one that closes the live defect.
        //   - ApprovalPolicyReader.GetCandidatePoliciesAsync filters only
        //     (TenantId [ambient], DocumentType, IsActive) with NO ProjectId predicate - a
        //     tenant-wide default and a project-scoped override for the same document type are both
        //     legitimate simultaneous candidates (that is the whole point of splitting them into two
        //     disjoint groups rather than one column), so that query is served by an index union
        //     over both indexes below, exactly as it would have needed a scan across both groups
        //     under the old single index (which never gave it a full 3-column seek either, since
        //     ProjectId was unrestricted there too).
        builder.HasIndex(p => new { p.TenantId, p.DocumentType })
            .IsUnique()
            .HasFilter("[IsActive] = 1 AND [ProjectId] IS NULL");

        // Project-scoped override: the original ADR-0008 guarantee, restricted to ProjectId IS NOT
        // NULL so it stays correct once overrides are actually created (schema-present since
        // Sprint 2, unexposed until a later sprint - see ApprovalPolicy.ProjectId's remarks). This
        // is the index that already behaved correctly pre-fix (SQLite/SQL Server both reject a
        // second active non-null-ProjectId row today) - unchanged in behaviour, only narrowed by an
        // explicit IS NOT NULL so the two indexes are provably disjoint.
        builder.HasIndex(p => new { p.TenantId, p.ProjectId, p.DocumentType })
            .IsUnique()
            .HasFilter("[IsActive] = 1 AND [ProjectId] IS NOT NULL");

        builder.ToTable(tb => tb.HasCheckConstraint(
            "CK_ApprovalPolicies_CumulativeVoEscalationPct", "[CumulativeVoEscalationPct] IS NULL OR [CumulativeVoEscalationPct] BETWEEN 0 AND 100"));

        // Version-pinned/immutable: no update path is exposed anywhere except Deactivate()
        // (IsActive/EffectiveTo only) - editing rules always creates a brand-new row via
        // CreateNextVersion, never an in-place edit (approval-workflow.md §5.2).
    }
}
