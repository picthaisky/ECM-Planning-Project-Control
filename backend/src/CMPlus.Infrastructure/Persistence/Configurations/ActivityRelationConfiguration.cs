using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

public sealed class ActivityRelationConfiguration : IEntityTypeConfiguration<ActivityRelation>
{
    public void Configure(EntityTypeBuilder<ActivityRelation> builder)
    {
        builder.ToTable("ActivityRelations");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.TenantId).IsRequired();

        // CPM forward/backward pass over 10,000+ activities/15,000+ relations must not scan
        // (docs/db-conventions.md §6).
        builder.HasIndex(r => new { r.TenantId, r.PredecessorActivityId });
        builder.HasIndex(r => new { r.TenantId, r.SuccessorActivityId });
    }
}
