using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for <see cref="Baseline"/> (S14-BE-01/S14-DB-01). "ProjectId is not an EF-level
/// FK" mirrors this codebase's existing convention for that column (<see cref="ActualCostEntry"/>/
/// <see cref="WBSNode"/>/<see cref="EvmPeriodSnapshot"/>/<see cref="CpmRun"/> all do the same) -
/// project existence is checked at the Application/repository layer instead.
/// </summary>
public sealed class BaselineConfiguration : IEntityTypeConfiguration<Baseline>
{
    public void Configure(EntityTypeBuilder<Baseline> builder)
    {
        builder.ToTable("Baselines");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();

        builder.Property(b => b.TenantId).IsRequired();
        builder.Property(b => b.Name).IsRequired().HasMaxLength(250);
        builder.Property(b => b.Bac).HasPrecision(18, 2);

        // Owned-collection navigation backed by the private `_snapshots` field -
        // BaselineActivitySnapshot has no public constructor/adder, so EF must populate it via the
        // backing field (CpmRun.Activities' established pattern). Restrict, not Cascade: an
        // IAppendOnly child is never legitimately deleted (docs/db-conventions.md §7) - this exists
        // purely as defense-in-depth against an out-of-band delete, never relied upon to happen.
        builder.HasMany(b => b.Snapshots)
            .WithOne()
            .HasForeignKey(s => s.BaselineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Metadata.FindNavigation(nameof(Baseline.Snapshots))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Computed, not a mapped column - see Baseline.ActivityCount's own remarks (a passthrough
        // over the already-loaded Snapshots collection, never queried independently).
        builder.Ignore(b => b.ActivityCount);

        // docs/10 §9 S14-BE-01/S14-DB-01 DoD: "หนึ่งโครงการมี IsActive=true ได้ชุดเดียว (บังคับที่
        // ระดับ DB ด้วย)" - exactly the same shape as ApprovalPolicyConfiguration's `(TenantId,
        // ProjectId, DocumentType) WHERE IsActive = 1` (ADR-0008), narrowed to `(TenantId,
        // ProjectId)` since a Baseline (unlike an ApprovalPolicy) has no further scoping dimension.
        // See Baseline's own remarks for what this environment can and cannot prove about it.
        builder.HasIndex(b => new { b.TenantId, b.ProjectId })
            .IsUnique()
            .HasFilter("[IsActive] = 1")
            .HasDatabaseName("IX_Baselines_TenantId_ProjectId_Active");

        // No further index here (e.g. "list this project's baselines by capture date") - this
        // change does not implement a list/management query, so an index with no query to serve
        // would be a guess. Left for database-engineer's S14-DB-01 pass once that read shape (and
        // S14-FE-01's actual access pattern) exists - see the backend-developer report.
    }
}
