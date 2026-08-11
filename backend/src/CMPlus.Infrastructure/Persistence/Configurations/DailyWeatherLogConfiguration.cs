using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for <see cref="DailyWeatherLog"/> (S11-BE-01/S11-DB-01), shaped by
/// <c>docs/specs/weather-eot/domain-rules.md</c> §8. "ProjectId is not an EF-level FK" mirrors this
/// codebase's existing convention for that column (<see cref="ActualCostEntry"/>/<see cref="WBSNode"/>/
/// <see cref="EvmPeriodSnapshot"/> all do the same) - project existence is checked at the
/// Application/repository layer instead.
/// </summary>
public sealed class DailyWeatherLogConfiguration : IEntityTypeConfiguration<DailyWeatherLog>
{
    public void Configure(EntityTypeBuilder<DailyWeatherLog> builder)
    {
        builder.ToTable("DailyWeatherLogs");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();

        builder.Property(w => w.TenantId).IsRequired();
        builder.Property(w => w.ConditionNote).HasMaxLength(200);
        builder.Property(w => w.RainfallMm).HasPrecision(6, 2);
        builder.Property(w => w.ImpactNote).HasMaxLength(500);
        builder.Property(w => w.HoursLost).HasPrecision(4, 2);
        builder.Property(w => w.CorrectionReason).HasMaxLength(500);

        // Self-referencing FK for the correction/supersession chain (§8.1/§8.2). Restrict, not
        // Cascade: an IAppendOnly row is never legitimately deleted (docs/db-conventions.md §7), so
        // this exists purely as defense-in-depth against an out-of-band delete leaving a superseding
        // row's reference dangling.
        builder.HasOne<DailyWeatherLog>()
            .WithMany()
            .HasForeignKey(w => w.CorrectsWeatherLogId)
            .OnDelete(DeleteBehavior.Restrict);

        // Owned-collection navigation backed by the private `_affectedActivities` field - the child
        // type has no public constructor/adder, so EF must populate it via the backing field
        // (VariationOrderConfiguration's established pattern for ApprovalSteps/ScopeItems).
        builder.HasMany(w => w.AffectedActivities)
            .WithOne()
            .HasForeignKey(a => a.DailyWeatherLogId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Metadata.FindNavigation(nameof(DailyWeatherLog.AffectedActivities))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // S11-DB-01 DoD: index (TenantId, ProjectId, LogDate); date-range queries as a seek.
        // database-engineer's tuning pass (confirmed via ToQueryString() against the real SqlServer
        // model, not just the DoD sentence): DailyWeatherLogRepository.ListByProjectAsync always
        // filters TenantId+ProjectId equality plus an optional LogDate >=/<= range - already a seek
        // against (TenantId, ProjectId, LogDate) regardless of ascending/descending declaration (SQL
        // Server traverses a B-tree range backward as cheaply as forward), so the literal DoD was
        // already satisfied. But the query's ORDER BY is 3 keys deep, not 1: the handler's own
        // .OrderByDescending(LogDate).ThenByDescending(RecordedAt) PLUS a third key, Id ascending,
        // that EF Core appends automatically and silently whenever a query Includes a collection
        // (AffectedActivities here) - needed to materialize the parent/child rows deterministically.
        // With LogDate as the only key column, ties on LogDate (expected and common: a correction
        // commonly keeps the original entry's LogDate per domain-rules.md §8.2 rule 6, so a project's
        // log routinely has 2-3 rows sharing one LogDate) force a Sort operator over the whole
        // date-range result to satisfy RecordedAt DESC among those ties. Extending the index to carry
        // RecordedAt DESC as a second key (matching CpmRunConfiguration's precedent of declaring index
        // direction to mirror the query's own ORDER BY) covers both ORDER BY keys the query actually
        // specifies inside the seek itself - no separate Sort operator for the common case. The
        // appended third key (Id, EF's own materialization tie-break) can still force a residual sort,
        // but only among rows sharing both LogDate and RecordedAt to the tick, which is a handful of
        // rows at most, never the whole result set. Row volume here does not approach
        // ActivityProgressLog/CpmRun scale (this is a human-authored daily site log, not a
        // per-activity machine record), so this is a genuine but modest refinement, not a correctness
        // fix - the seek itself was already correct.
        builder.HasIndex(w => new { w.TenantId, w.ProjectId, w.LogDate, w.RecordedAt })
            .IsDescending(false, false, true, true);

        // domain-rules.md §8.2 chain-integrity rule 2 - the load-bearing one: "at most one entry may
        // point at any given entry". Filtered (most rows are Original and carry a null
        // CorrectsWeatherLogId, which a plain unique index would otherwise collide on). This is the
        // authoritative backstop behind RecordWeatherLogCorrectionCommandHandler's own pre-check
        // (409 WeatherLogAlreadySuperseded) - same "pre-check + index" pattern as VariationOrder.VoNumber.
        builder.HasIndex(w => new { w.TenantId, w.CorrectsWeatherLogId })
            .IsUnique()
            .HasFilter("[CorrectsWeatherLogId] IS NOT NULL");

        builder.ToTable(tb =>
        {
            tb.HasCheckConstraint("CK_DailyWeatherLogs_RainfallMm", "[RainfallMm] IS NULL OR [RainfallMm] >= 0");
            tb.HasCheckConstraint("CK_DailyWeatherLogs_HoursLost", "[HoursLost] IS NULL OR [HoursLost] BETWEEN 0 AND 24");
        });

        // Append-only (S11-BE-01 DoD / docs/db-conventions.md §7): no update/delete path exists on
        // the entity (no mutator methods/setters) or here in configuration. Structurally enforced by
        // AppendOnlyGuardInterceptor via the IAppendOnly marker (security review sprint-09.md M-01) -
        // see DailyWeatherLog's own class remarks.
    }
}
