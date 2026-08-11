using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="ProjectEotPolicy"/> (S11-BE-02, domain-rules.md
/// weather-eot §3.5). "ProjectId is not an EF-level FK" mirrors this codebase's existing convention
/// for that column (<see cref="DailyWeatherLog"/>/<see cref="CpmRun"/> both do the same).</summary>
public sealed class ProjectEotPolicyConfiguration : IEntityTypeConfiguration<ProjectEotPolicy>
{
    public void Configure(EntityTypeBuilder<ProjectEotPolicy> builder)
    {
        builder.ToTable("ProjectEotPolicies");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.FullDayHours).HasPrecision(4, 2);
        builder.Property(p => p.MinHoursLostForCountableDay).HasPrecision(4, 2);
        builder.Property(p => p.MinRainfallMmForCountableDay).HasPrecision(6, 2);

        // 1:1 with Project (§3.5) - at most one policy row per project.
        builder.HasIndex(p => new { p.TenantId, p.ProjectId }).IsUnique();

        builder.ToTable(tb =>
        {
            tb.HasCheckConstraint("CK_ProjectEotPolicies_FullDayHours", "[FullDayHours] > 0 AND [FullDayHours] <= 24");
            tb.HasCheckConstraint(
                "CK_ProjectEotPolicies_MinHoursLostForCountableDay", "[MinHoursLostForCountableDay] >= 0 AND [MinHoursLostForCountableDay] <= 24");
            tb.HasCheckConstraint(
                "CK_ProjectEotPolicies_MinRainfallMmForCountableDay", "[MinRainfallMmForCountableDay] IS NULL OR [MinRainfallMmForCountableDay] >= 0");
            tb.HasCheckConstraint("CK_ProjectEotPolicies_NoticePeriodDays", "[NoticePeriodDays] IS NULL OR [NoticePeriodDays] >= 0");
        });
    }
}
