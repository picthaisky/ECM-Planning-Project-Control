using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="WorkCategory"/> - domain-rules.md (manpower-equipment)
/// §4.3. <c>ProjectId</c> is deliberately not an EF-level FK relationship, matching this codebase's
/// existing convention for that column (<see cref="WBSNode"/>/<see cref="ActualCostEntry"/> reference
/// it the same way) - project existence is checked at the Application/repository layer instead.</summary>
public sealed class WorkCategoryConfiguration : IEntityTypeConfiguration<WorkCategory>
{
    public void Configure(EntityTypeBuilder<WorkCategory> builder)
    {
        builder.ToTable("WorkCategories");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();

        builder.Property(w => w.TenantId).IsRequired();
        builder.Property(w => w.Code).HasMaxLength(16).IsRequired();
        builder.Property(w => w.NameTh).HasMaxLength(100).IsRequired();
        builder.Property(w => w.NameEn).HasMaxLength(100).IsRequired();

        // §4.3: NULL ProjectId = tenant-wide default catalogue entry; non-null = a project's own
        // override - both read through the same (TenantId, ProjectId) seek.
        builder.HasIndex(w => new { w.TenantId, w.ProjectId });

        builder.HasIndex(w => new { w.TenantId, w.Code });
    }
}
