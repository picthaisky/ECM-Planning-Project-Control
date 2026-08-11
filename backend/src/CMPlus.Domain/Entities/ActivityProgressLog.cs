using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// Append-only, immutable system-of-record for progress-over-time (ADR-0009). Deliberately has no
/// update/delete method - corrections are new rows, never edits. The constructor is
/// <c>internal</c> so the only way to create an instance is <see cref="Activity.RecordProgress"/>;
/// this makes "no path writes the cache/log directly" a structural guarantee, not just a
/// convention, since Application/Infrastructure code outside this assembly cannot call
/// <c>new ActivityProgressLog(...)</c> at all.
///
/// <para>The <see cref="IAppendOnly"/> marker closes the remaining half. An <c>internal</c>
/// constructor stops anything <i>creating</i> a row improperly, but it says nothing about an
/// ordinary <c>DbContext</c> rewriting or deleting one that already exists — precisely the gap
/// Sprint 9's M-01 found (execution-verified on <c>ApprovalAction</c>, whose own comment likewise
/// claimed append-only). This entity kept that gap until Sprint 11, when it was spotted while
/// building `DailyWeatherLog` to the same pattern. The marker makes the guarantee structural.</para>
/// </summary>
public sealed class ActivityProgressLog : Entity, ITenantOwned, IAppendOnly
{
    public Guid TenantId { get; private set; }

    public Guid ActivityId { get; private set; }

    public DateTimeOffset PeriodEndDate { get; private set; }

    public decimal ProgressPercentage { get; private set; }

    /// <summary>Null when not supplied by the recorder - never defaulted to 0 (S1-BE-02 DoD).</summary>
    public decimal? ActualQuantity { get; private set; }

    public Guid RecordedByUserId { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public ProgressSource Source { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private ActivityProgressLog()
    {
    }

    internal ActivityProgressLog(
        Guid tenantId,
        Guid activityId,
        DateTimeOffset periodEndDate,
        decimal progressPercentage,
        decimal? actualQuantity,
        Guid recordedByUserId,
        DateTimeOffset recordedAt,
        ProgressSource source)
    {
        TenantId = tenantId;
        ActivityId = activityId;
        PeriodEndDate = periodEndDate;
        ProgressPercentage = progressPercentage; // already clamped by Activity.RecordProgress
        ActualQuantity = actualQuantity;
        RecordedByUserId = recordedByUserId;
        RecordedAt = recordedAt;
        Source = source;
    }
}
