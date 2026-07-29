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
/// </summary>
public sealed class ActivityProgressLog : Entity, ITenantOwned
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
