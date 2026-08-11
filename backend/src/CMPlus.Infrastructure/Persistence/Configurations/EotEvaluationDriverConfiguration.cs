using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="EotEvaluationDriver"/> (S11-BE-02, domain-rules.md
/// weather-eot §5.4). <c>CpmRunId</c>/<c>ActivityId</c> are not EF-level FKs - same convention as
/// every other row that cites those aggregates by id without owning their lifecycle.</summary>
public sealed class EotEvaluationDriverConfiguration : IEntityTypeConfiguration<EotEvaluationDriver>
{
    public void Configure(EntityTypeBuilder<EotEvaluationDriver> builder)
    {
        builder.ToTable("EotEvaluationDrivers");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.TenantId).IsRequired();
        // Same lengths as ActivityConfiguration's own ActivityCode/Name - this is a read-only copy
        // of that data at evaluation time, not an independent business field.
        builder.Property(d => d.ActivityCode).HasMaxLength(50).IsRequired();
        builder.Property(d => d.ActivityName).HasMaxLength(250).IsRequired();
        builder.Property(d => d.UnclaimedFractionalHours).HasPrecision(6, 2);
        // domain-rules.md §5.4: "the distinct codes that absorbed them" - a handful of ActivityCodes
        // (HasMaxLength(50) each, above) joined for display; 200 is the field's own documented length.
        builder.Property(d => d.AbsorbedIntoActivityCodes).HasMaxLength(200);

        builder.HasIndex(d => new { d.TenantId, d.EotEvaluationId });

        builder.ToTable(tb => tb.HasCheckConstraint(
            "CK_EotEvaluationDrivers_Counts",
            "[StoppageDays] >= 0 AND [IndicativeEotDays] >= 0 AND [MarginalEotDays] >= 0 "
            + "AND [RemainingFloatAfter] >= 0 AND [SerialChainAbsorbedDays] >= 0"));
    }
}
