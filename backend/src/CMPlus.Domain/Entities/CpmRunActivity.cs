using CMPlus.Domain.Common;

namespace CMPlus.Domain.Entities;

/// <summary>
/// One activity's CPM result within a captured <see cref="CpmRun"/> (ADR-0019, domain-rules.md
/// weather-eot §4.3) - the historical counterpart of the three live fields
/// <see cref="Activity.IsCritical"/>/<see cref="Activity.TotalFloat"/>/<see cref="Activity.FreeFloat"/>
/// overwrite on every recalculation, plus the four day-count fields
/// (<c>CMPlus.Application.Services.Cpm.CpmActivityResult</c>'s own shape) those live fields never
/// kept at all.
///
/// <para><see cref="IAppendOnly"/>, the same tight guarantee <see cref="DailyWeatherLogActivity"/>
/// uses: there is no legitimate "clear and re-add" lifecycle for a captured run's children - a run
/// is immutable in its entirety, not field-by-field. The constructor is <c>internal</c> so the only
/// way to obtain an instance is through <see cref="CpmRun.Capture"/>.</para>
/// </summary>
public sealed class CpmRunActivity : Entity, ITenantOwned, IAppendOnly
{
    public Guid TenantId { get; private set; }

    public Guid CpmRunId { get; private set; }

    public Guid ActivityId { get; private set; }

    public int DurationDays { get; private set; }

    public int EarlyStart { get; private set; }

    public int EarlyFinish { get; private set; }

    public int LateStart { get; private set; }

    public int LateFinish { get; private set; }

    /// <summary>The float this activity had <b>at this run</b> - domain-rules.md §5.4's
    /// $TF_j^{(r)}$, never to be confused with the live, since-overwritten
    /// <see cref="Activity.TotalFloat"/>. Not constrained to be non-negative here: the shipped
    /// <c>CpmEngine</c> only ever produces $TF \ge 0$ today (its $LF_{project} = \max EF$ rule),
    /// but domain-rules.md §4.5 flags that a future completion-constraint change would legitimately
    /// make this negative, and this row must be able to store whatever the engine actually computed
    /// for that run without a schema change.</summary>
    public int TotalFloat { get; private set; }

    public int FreeFloat { get; private set; }

    public bool IsCritical { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private CpmRunActivity()
    {
    }

    internal CpmRunActivity(Guid tenantId, Guid cpmRunId, CpmRunActivityInput input)
    {
        if (cpmRunId == Guid.Empty)
        {
            throw new DomainException("CpmRunActivity.CpmRunId is required.");
        }

        if (input.ActivityId == Guid.Empty)
        {
            throw new DomainException("CpmRunActivity.ActivityId is required.");
        }

        if (input.DurationDays < 0)
        {
            throw new DomainException("CpmRunActivity.DurationDays cannot be negative.");
        }

        TenantId = tenantId;
        CpmRunId = cpmRunId;
        ActivityId = input.ActivityId;
        DurationDays = input.DurationDays;
        EarlyStart = input.EarlyStart;
        EarlyFinish = input.EarlyFinish;
        LateStart = input.LateStart;
        LateFinish = input.LateFinish;
        TotalFloat = input.TotalFloat;
        FreeFloat = input.FreeFloat;
        IsCritical = input.IsCritical;
    }
}
