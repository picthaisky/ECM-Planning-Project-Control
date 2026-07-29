using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

public sealed class ActivityProgressLogConfiguration : IEntityTypeConfiguration<ActivityProgressLog>
{
    public void Configure(EntityTypeBuilder<ActivityProgressLog> builder)
    {
        builder.ToTable("ActivityProgressLogs");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.TenantId).IsRequired();
        builder.Property(l => l.ProgressPercentage).HasPrecision(5, 2);
        builder.Property(l => l.ActualQuantity).HasPrecision(18, 2);

        builder.HasOne<Activity>()
            .WithMany()
            .HasForeignKey(l => l.ActivityId)
            .OnDelete(DeleteBehavior.Restrict);

        // ADR-0009 mandatory indexes (docs/db-conventions.md §6): the step-function read
        // (GetProgressAsOf) must be an index seek on (TenantId, ActivityId, PeriodEndDate DESC),
        // plus a period-rollup index.
        builder.HasIndex(l => new { l.TenantId, l.ActivityId, l.PeriodEndDate })
            .IsDescending(false, false, true);

        builder.HasIndex(l => new { l.TenantId, l.PeriodEndDate });

        // CHECK constraints: DB-layer defense-in-depth mirroring the Domain invariants
        // (docs/db-conventions.md §3.1). ActualQuantity stays nullable-safe (NULL satisfies CHECK).
        builder.ToTable(tb =>
        {
            tb.HasCheckConstraint("CK_ActivityProgressLogs_ProgressPercentage", "[ProgressPercentage] BETWEEN 0 AND 100");
            tb.HasCheckConstraint("CK_ActivityProgressLogs_ActualQuantity", "[ActualQuantity] >= 0");
        });

        // Append-only (docs/db-conventions.md §7): no update/delete path exists on the entity
        // (constructor is internal, no mutator methods) or here in configuration.
    }
}
