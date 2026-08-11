using CMPlus.Domain.Common;

namespace CMPlus.Domain.Entities;

/// <summary>
/// One activity's frozen planned dates/duration/budget within a captured <see cref="Baseline"/>
/// (docs/10 §9 S14-BE-01/§3's explicit field list: "Id, TenantId, BaselineId, ActivityId,
/// PlannedStart/Finish, DurationDays, BudgetCost") - the historical counterpart of the live,
/// continually-overwritten <see cref="Activity.PlannedStart"/>/<see cref="Activity.PlannedFinish"/>/
/// <see cref="Activity.DurationDays"/>/<see cref="Activity.BudgetCost"/>, exactly as
/// <see cref="CpmRunActivity"/> is the historical counterpart of <see cref="Activity.IsCritical"/>/
/// <see cref="Activity.TotalFloat"/>/<see cref="Activity.FreeFloat"/>.
///
/// <para><see cref="IAppendOnly"/> - "Baseline snapshots are historical evidence" (this task's
/// brief): a reference point a delta comparison is measured against must never move once captured,
/// or every past comparison silently becomes unreproducible/unexplainable - the same reasoning
/// <see cref="CpmRunActivity"/>/<see cref="EvmPeriodSnapshot"/> already establish for their own
/// frozen rows. Unlike the owning <see cref="Baseline"/> itself (which legitimately needs
/// <see cref="Baseline.IsActive"/> to stay mutable for the project's whole life and therefore
/// cannot carry this marker - see that type's own remarks), nothing on this child row is ever
/// legitimately revised: a correction is a brand new <see cref="Baseline"/> (a fresh
/// "+ บันทึก Baseline ใหม่"), never an edit of an existing snapshot row. The constructor is
/// <c>internal</c> so the only way to obtain an instance is through <see cref="Baseline.Capture"/>.
/// </para>
/// </summary>
public sealed class BaselineActivitySnapshot : Entity, ITenantOwned, IAppendOnly
{
    public Guid TenantId { get; private set; }

    public Guid BaselineId { get; private set; }

    public Guid ActivityId { get; private set; }

    public DateTimeOffset PlannedStart { get; private set; }

    public DateTimeOffset PlannedFinish { get; private set; }

    public int DurationDays { get; private set; }

    public decimal BudgetCost { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private BaselineActivitySnapshot()
    {
    }

    internal BaselineActivitySnapshot(Guid tenantId, Guid baselineId, BaselineActivitySnapshotInput input)
    {
        if (baselineId == Guid.Empty)
        {
            throw new DomainException("BaselineActivitySnapshot.BaselineId is required.");
        }

        if (input.ActivityId == Guid.Empty)
        {
            throw new DomainException("BaselineActivitySnapshot.ActivityId is required.");
        }

        if (input.DurationDays < 0)
        {
            throw new DomainException("BaselineActivitySnapshot.DurationDays cannot be negative.");
        }

        TenantId = tenantId;
        BaselineId = baselineId;
        ActivityId = input.ActivityId;
        PlannedStart = input.PlannedStart;
        PlannedFinish = input.PlannedFinish;
        DurationDays = input.DurationDays;
        BudgetCost = MoneyGuard.EnsureNonNegative(input.BudgetCost, nameof(BudgetCost));
    }
}
