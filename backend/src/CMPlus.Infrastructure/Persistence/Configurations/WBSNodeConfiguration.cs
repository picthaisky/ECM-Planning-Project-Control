using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

public sealed class WBSNodeConfiguration : IEntityTypeConfiguration<WBSNode>
{
    public void Configure(EntityTypeBuilder<WBSNode> builder)
    {
        builder.ToTable("WBSNodes");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();

        builder.Property(n => n.TenantId).IsRequired();
        builder.Property(n => n.Code).HasMaxLength(50).IsRequired();
        builder.Property(n => n.Title).HasMaxLength(250).IsRequired();
        builder.Property(n => n.WeightPercentage).HasPrecision(5, 2);

        // Self-referencing FK; no navigation properties on the entity by design (plain scalar
        // Guid? per docs/db-conventions.md §1). Restrict delete - removing a parent with children
        // must go through an explicit re-parent/delete workflow, never a silent cascade.
        builder.HasOne<WBSNode>()
            .WithMany()
            .HasForeignKey(n => n.ParentWbsNodeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Mandatory index for WBS tree reads, < 100 ms budget (docs/db-conventions.md §6).
        // INCLUDE Code/Title/WeightPercentage (S4-DB-01 finding): WbsTreeReader's node-shape query
        // projects exactly {Id, ParentWbsNodeId, Code, Title, WeightPercentage} - without these as
        // included columns, a seek on (TenantId, ProjectId, ParentWbsNodeId) still needs one
        // key-lookup per matching row back to the clustered index, so at real multi-project volume
        // (verified at 50,000 WBSNodes across 11 projects, this project's own 5,000 = 10% of the
        // table) the optimizer chose a full Clustered Index Scan over seek+5,000 lookups - a table
        // scan whose cost grows with the *whole tenant base's* WBSNode count, not this project's,
        // directly violating the "single-round-trip hierarchical reads" / "< 100 ms" requirements.
        // Making the index covering removes the lookup entirely, so the seek satisfies the query on
        // its own - verified via SET STATISTICS XML ON showing Index Seek instead of Clustered Index
        // Scan after this change (S4-DB-01 report).
        builder.HasIndex(n => new { n.TenantId, n.ProjectId, n.ParentWbsNodeId })
            .IncludeProperties(n => new { n.Code, n.Title, n.WeightPercentage });

        // CHECK constraint: DB-layer defense-in-depth mirroring the Domain clamp (docs/db-conventions.md §3.1).
        builder.ToTable(tb => tb.HasCheckConstraint("CK_WBSNodes_WeightPercentage", "[WeightPercentage] BETWEEN 0 AND 100"));
    }
}
