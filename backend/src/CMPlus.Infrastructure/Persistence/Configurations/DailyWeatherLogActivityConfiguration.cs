using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

public sealed class DailyWeatherLogActivityConfiguration : IEntityTypeConfiguration<DailyWeatherLogActivity>
{
    public void Configure(EntityTypeBuilder<DailyWeatherLogActivity> builder)
    {
        builder.ToTable("DailyWeatherLogActivities");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.TenantId).IsRequired();

        builder.HasOne<Activity>()
            .WithMany()
            .HasForeignKey(a => a.ActivityId)
            .OnDelete(DeleteBehavior.Restrict);

        // One row per (log, activity) pair - RecordWeatherLogCommandValidator also rejects a
        // duplicate id within one request; this is the persistence-layer backstop.
        builder.HasIndex(a => new { a.TenantId, a.DailyWeatherLogId, a.ActivityId }).IsUnique();

        // Reverse-lookup direction ("which weather stoppages tagged this activity") for the
        // not-yet-built EotEvaluator (S11-BE-02, out of this task's scope) - costs nothing to add
        // now and avoids a scan the day that query is written.
        builder.HasIndex(a => new { a.TenantId, a.ActivityId });

        // Append-only, tighter than its parent even needs (see the entity's own remarks) - no
        // update/delete path exists on the entity or here in configuration.
    }
}
