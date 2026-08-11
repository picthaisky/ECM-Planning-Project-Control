using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="BaselineActivitySnapshot"/> (S14-BE-01/S14-DB-01).
/// <c>ActivityId</c> is not an EF-level FK, same convention as every other history/log row that
/// references an <see cref="Activity"/> (<see cref="ActivityProgressLog"/>/<see cref="CpmRunActivity"/>) -
/// the referenced Activity's own lifecycle is independent of this append-only snapshot row (and may
/// legitimately have no matching live Activity at all once a delete path exists in the future - see
/// <c>IBaselineComparisonReader</c>'s remarks).</summary>
public sealed class BaselineActivitySnapshotConfiguration : IEntityTypeConfiguration<BaselineActivitySnapshot>
{
    public void Configure(EntityTypeBuilder<BaselineActivitySnapshot> builder)
    {
        builder.ToTable("BaselineActivitySnapshots");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.BudgetCost).HasPrecision(18, 2);

        // docs/10 §9 S14-DB-01 DoD: "index (TenantId, BaselineId, ActivityId)". Unique: a baseline
        // captures each activity exactly once by construction (Baseline.Capture iterates the
        // project's current activities once), so a second snapshot row for the same
        // (BaselineId, ActivityId) pair is a data-integrity bug, not a legitimate state - the same
        // "enforce the invariant at the DB level too" discipline this sprint's brief applies to
        // Baseline.IsActive. Also the exact index CompareBaselineQueryHandler's
        // `Where(s => s.BaselineId == baselineId)` join seeks on (TenantId leads via the global
        // query filter, BaselineId is the next column). database-engineer's S14-DB-01 owns
        // confirming this plan is a seek, not a scan, at 10,000-row/baseline volume against real SQL
        // Server, and the row-size/volume estimate this environment cannot measure (Docker cannot
        // start - docs/perf/gantt-frontend-s6.md §3).
        builder.HasIndex(s => new { s.TenantId, s.BaselineId, s.ActivityId })
            .IsUnique();

        builder.ToTable(tb =>
            tb.HasCheckConstraint("CK_BaselineActivitySnapshots_DurationDays", "[DurationDays] >= 0"));

        // Append-only (this task's brief: "Baseline snapshots are historical evidence"): no
        // update/delete path exists on the entity (no mutator methods/setters) or here in
        // configuration. Structurally enforced by AppendOnlyGuardInterceptor via the IAppendOnly
        // marker (security review sprint-09.md M-01 pattern) - see Baseline's own remarks for why
        // the owning Baseline itself is deliberately NOT also IAppendOnly.
    }
}
