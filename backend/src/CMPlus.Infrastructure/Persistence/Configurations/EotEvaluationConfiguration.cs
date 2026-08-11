using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for <see cref="EotEvaluation"/> (S11-BE-02, domain-rules.md weather-eot §8.5).
/// "ProjectId is not an EF-level FK" mirrors <see cref="DailyWeatherLog"/>/<see cref="CpmRun"/>'s
/// existing convention. Owned-collection navigations follow <see cref="CpmRun"/>'s established
/// pattern exactly (backing-field access, <c>Restrict</c> delete).
/// </summary>
public sealed class EotEvaluationConfiguration : IEntityTypeConfiguration<EotEvaluation>
{
    public void Configure(EntityTypeBuilder<EotEvaluation> builder)
    {
        builder.ToTable("EotEvaluations");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.TenantId).IsRequired();
        // PolicySnapshotJson: no HasMaxLength -> nvarchar(max), per domain-rules.md §8.5's field list.
        builder.Property(e => e.PolicySnapshotJson).IsRequired();

        builder.HasMany(e => e.Runs)
            .WithOne()
            .HasForeignKey(r => r.EotEvaluationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Metadata.FindNavigation(nameof(EotEvaluation.Runs))!.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(e => e.Sources)
            .WithOne()
            .HasForeignKey(s => s.EotEvaluationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Metadata.FindNavigation(nameof(EotEvaluation.Sources))!.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(e => e.Drivers)
            .WithOne()
            .HasForeignKey(d => d.EotEvaluationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Metadata.FindNavigation(nameof(EotEvaluation.Drivers))!.SetPropertyAccessMode(PropertyAccessMode.Field);

        // History/staleness reads ("latest evaluation for this project") are a seek against this.
        builder.HasIndex(e => new { e.TenantId, e.ProjectId, e.EvaluatedAt });

        builder.ToTable(tb =>
        {
            tb.HasCheckConstraint("CK_EotEvaluations_Window", "[WindowEnd] >= [WindowStart]");
            tb.HasCheckConstraint(
                "CK_EotEvaluations_Durations",
                "[AsScheduledDurationDays] >= 0 AND [ImpactedDurationDays] >= 0 AND [EotEligibleDays] >= 0");
            tb.HasCheckConstraint(
                "CK_EotEvaluations_Counts",
                "[CountableStoppageDayCount] >= 0 AND [SerialChainAbsorbedDayCount] >= 0 "
                + "AND [DistinctCountableDateCount] >= 0 AND [UnattributedStoppageDayCount] >= 0");
        });

        // Append-only (§2.1/§8.5 rule 1: "a correction never mutates a stored evaluation"): no
        // update/delete path exists on the entity or here. Structurally enforced by
        // AppendOnlyGuardInterceptor via the IAppendOnly marker.
    }
}
