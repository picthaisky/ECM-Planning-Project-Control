using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="CpmRunActivity"/> (ADR-0019, domain-rules.md
/// weather-eot §4.3). <c>ActivityId</c> is not an EF-level FK, same convention as every other
/// history/log row that references an <see cref="Activity"/> (<see cref="ActivityProgressLog"/>,
/// <see cref="DailyWeatherLogActivity"/>) - the referenced Activity's own lifecycle is independent
/// of this append-only snapshot row.</summary>
public sealed class CpmRunActivityConfiguration : IEntityTypeConfiguration<CpmRunActivity>
{
    public void Configure(EntityTypeBuilder<CpmRunActivity> builder)
    {
        builder.ToTable("CpmRunActivities");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.TenantId).IsRequired();

        // The evaluator's primary access path - "give me this run's activities" - is satisfied via
        // CpmRun.Activities (ICpmRunHistoryReader.GetGoverningRunAsync's Include). This index backs
        // that Include/any direct CpmRunId lookup as a seek rather than a scan.
        builder.HasIndex(a => new { a.TenantId, a.CpmRunId });

        builder.ToTable(tb =>
            tb.HasCheckConstraint("CK_CpmRunActivities_DurationDays", "[DurationDays] >= 0"));

        // Append-only (ADR-0019): no update/delete path exists on the entity (no mutator methods/
        // setters) or here in configuration. Structurally enforced by AppendOnlyGuardInterceptor via
        // the IAppendOnly marker (security review sprint-09.md M-01 pattern).
    }
}
