using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for <see cref="CpmRun"/> (ADR-0019, domain-rules.md weather-eot §4.3).
/// "ProjectId is not an EF-level FK" mirrors this codebase's existing convention for that column
/// (<see cref="ActualCostEntry"/>/<see cref="WBSNode"/>/<see cref="EvmPeriodSnapshot"/>/
/// <see cref="DailyWeatherLog"/> all do the same) - project existence is checked at the
/// Application/repository layer instead.
/// </summary>
public sealed class CpmRunConfiguration : IEntityTypeConfiguration<CpmRun>
{
    public void Configure(EntityTypeBuilder<CpmRun> builder)
    {
        builder.ToTable("CpmRuns");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.TenantId).IsRequired();

        // Owned-collection navigations backed by the private `_activities`/`_relations` fields -
        // neither child type has a public constructor/adder, so EF must populate them via the
        // backing field (DailyWeatherLog.AffectedActivities' established pattern). Restrict, not
        // Cascade: an IAppendOnly row is never legitimately deleted (docs/db-conventions.md §7), so
        // this exists purely as defense-in-depth against an out-of-band delete leaving a child row
        // orphaned - never relied upon to actually happen, and deliberately does not make bulk
        // deletion of a run's children easy (ADR-0019's retention note: pruning must stay a
        // deliberate, citation-aware maintenance operation, never an accidental cascade).
        builder.HasMany(r => r.Activities)
            .WithOne()
            .HasForeignKey(a => a.CpmRunId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Metadata.FindNavigation(nameof(CpmRun.Activities))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(r => r.Relations)
            .WithOne()
            .HasForeignKey(rel => rel.CpmRunId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Metadata.FindNavigation(nameof(CpmRun.Relations))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // domain-rules.md §4.3's mandatory index: the governing-run lookup
        // (ICpmRunHistoryReader.GetGoverningRunAsync, r(d) = argmax{CalculatedAt <= d}) is a seek
        // against this. database-engineer owns any further tuning under the re-scoped S11-DB-01.
        builder.HasIndex(r => new { r.TenantId, r.ProjectId, r.CalculatedAt })
            .IsDescending(false, false, true);

        builder.ToTable(tb =>
            tb.HasCheckConstraint("CK_CpmRuns_ProjectDurationDays", "[ProjectDurationDays] >= 0"));

        // Append-only (ADR-0019): no update/delete path exists on the entity (no mutator methods/
        // setters) or here in configuration. Structurally enforced by AppendOnlyGuardInterceptor via
        // the IAppendOnly marker (security review sprint-09.md M-01 pattern).
    }
}
