using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

public sealed class ActivityConfiguration : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> builder)
    {
        builder.ToTable("Activities");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.TenantId).IsRequired();
        builder.Property(a => a.ActivityCode).HasMaxLength(50).IsRequired();
        builder.Property(a => a.Name).HasMaxLength(250).IsRequired();
        builder.Property(a => a.BudgetCost).HasPrecision(18, 2);
        builder.Property(a => a.ProgressPercentage).HasPrecision(5, 2);

        builder.HasIndex(a => new { a.TenantId, a.WbsNodeId });

        // CHECK constraints: DB-layer defense-in-depth mirroring the Domain invariants
        // (docs/db-conventions.md §3.1).
        builder.ToTable(tb =>
        {
            tb.HasCheckConstraint("CK_Activities_BudgetCost", "[BudgetCost] >= 0");
            tb.HasCheckConstraint("CK_Activities_ProgressPercentage", "[ProgressPercentage] BETWEEN 0 AND 100");
            tb.HasCheckConstraint("CK_Activities_DurationDays", "[DurationDays] >= 0");
        });
    }
}
