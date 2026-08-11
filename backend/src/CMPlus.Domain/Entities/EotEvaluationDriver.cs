using CMPlus.Domain.Common;

namespace CMPlus.Domain.Entities;

/// <summary>
/// domain-rules.md weather-eot §5.4's explainability row - "อธิบายได้ว่าอ้างกิจกรรมใด". One row per
/// (governing <see cref="CpmRun"/>, <see cref="Activity"/>) pair that had at least one countable
/// stoppage day charged to it within an <see cref="EotEvaluation"/>. <see cref="CpmRunId"/> (rather
/// than a freshly-generated <c>EotEvaluationRunId</c>) is the correlation key back to the matching
/// <see cref="EotEvaluationRun"/> row - see <see cref="EotEvaluationDriverInput"/>'s own remarks for
/// why. <b>Neither this row nor its siblings are required to sum to <see cref="EotEvaluation.EotEligibleDays"/>
/// - the network figure always governs</b> (§5.4, fixture W-07).
///
/// <para><see cref="IAppendOnly"/> - constructor is <c>internal</c>, reachable only through
/// <see cref="EotEvaluation.Capture"/>. <see cref="ActivityId"/>/<see cref="ActivityCode"/>/
/// <see cref="ActivityName"/> are read-only evidence copied at evaluation time, never a write path
/// onto <see cref="Activity"/> itself (§2.1's zero-side-effect boundary).</para>
/// </summary>
public sealed class EotEvaluationDriver : Entity, ITenantOwned, IAppendOnly
{
    public Guid TenantId { get; private set; }

    public Guid EotEvaluationId { get; private set; }

    public Guid CpmRunId { get; private set; }

    public Guid ActivityId { get; private set; }

    public string ActivityCode { get; private set; } = string.Empty;

    public string ActivityName { get; private set; } = string.Empty;

    /// <summary>$S_j$ - countable days charged to this activity under this run, after the §3.7
    /// per-activity performance-window gate and (under <see cref="Enums.EotPartialDayPolicy.FractionalAccrual"/>)
    /// the per-run floor.</summary>
    public int StoppageDays { get; private set; }

    /// <summary>$TF_j^{(r)}$ - the float this activity had <b>at the governing run</b>, never the
    /// live, since-overwritten <see cref="Activity.TotalFloat"/>. Not constrained non-negative here -
    /// mirrors <see cref="CpmRunActivity.TotalFloat"/>'s own remark on the §4.5 negative-float
    /// caveat.</summary>
    public int TotalFloatAtRun { get; private set; }

    /// <summary>$TF_j^{(r)} = 0$.</summary>
    public bool WasCriticalAtRun { get; private set; }

    /// <summary>Critical in the <b>impacted</b> network - how criticality shift becomes visible
    /// (fixture W-03).</summary>
    public bool IsOnImpactedCriticalPath { get; private set; }

    /// <summary>$\max(0, S_j - TF_j^{(r)})$ - the single-activity closed form, exact only when this
    /// activity is the sole one affected (§5.4). Not the algorithm.</summary>
    public int IndicativeEotDays { get; private set; }

    /// <summary>$D^{imp}_r - D^{imp \setminus j}_r$ - re-run with only this activity's stoppages
    /// removed.</summary>
    public int MarginalEotDays { get; private set; }

    /// <summary>$\max(0, TF_j^{(r)} - S_j)$.</summary>
    public int RemainingFloatAfter { get; private set; }

    /// <summary>§3.4: only meaningful under <see cref="Enums.EotPartialDayPolicy.FractionalAccrual"/> -
    /// the fractional hours left over after this run's per-activity floor, reported for
    /// transparency and never carried to another activity or a later evaluation.</summary>
    public decimal? UnclaimedFractionalHours { get; private set; }

    /// <summary>domain-rules.md §5.2a/§5.4: countable days recorded against this activity that the
    /// serial-chain collapse absorbed into another, serially-chained activity on the same governing
    /// run - never deleted, never given an <see cref="Enums.EotExclusionReason"/>. <c>0</c> for every
    /// domain-rules.md fixture except W-17 and W-20. <see cref="StoppageDays"/> +
    /// <see cref="SerialChainAbsorbedDays"/> is the countable days the *evidence* records against this
    /// activity; <see cref="StoppageDays"/> alone is what the *schedule* charged.</summary>
    public int SerialChainAbsorbedDays { get; private set; }

    /// <summary><c>nvarchar(200)</c>. The distinct <c>ActivityCode</c>s that absorbed this activity's
    /// days, in topological order - <see langword="null"/> iff <see cref="SerialChainAbsorbedDays"/>
    /// is 0 (§5.4).</summary>
    public string? AbsorbedIntoActivityCodes { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private EotEvaluationDriver()
    {
    }

    internal EotEvaluationDriver(Guid tenantId, Guid eotEvaluationId, EotEvaluationDriverInput input)
    {
        if (eotEvaluationId == Guid.Empty)
        {
            throw new DomainException("EotEvaluationDriver.EotEvaluationId is required.");
        }

        if (input.CpmRunId == Guid.Empty)
        {
            throw new DomainException("EotEvaluationDriver.CpmRunId is required.");
        }

        if (input.ActivityId == Guid.Empty)
        {
            throw new DomainException("EotEvaluationDriver.ActivityId is required.");
        }

        if (input.StoppageDays < 0 || input.IndicativeEotDays < 0 || input.MarginalEotDays < 0
            || input.RemainingFloatAfter < 0 || input.SerialChainAbsorbedDays < 0)
        {
            throw new DomainException("EotEvaluationDriver day-count fields cannot be negative.");
        }

        TenantId = tenantId;
        EotEvaluationId = eotEvaluationId;
        CpmRunId = input.CpmRunId;
        ActivityId = input.ActivityId;
        ActivityCode = input.ActivityCode;
        ActivityName = input.ActivityName;
        StoppageDays = input.StoppageDays;
        TotalFloatAtRun = input.TotalFloatAtRun;
        WasCriticalAtRun = input.WasCriticalAtRun;
        IsOnImpactedCriticalPath = input.IsOnImpactedCriticalPath;
        IndicativeEotDays = input.IndicativeEotDays;
        MarginalEotDays = input.MarginalEotDays;
        RemainingFloatAfter = input.RemainingFloatAfter;
        UnclaimedFractionalHours = input.UnclaimedFractionalHours;
        SerialChainAbsorbedDays = input.SerialChainAbsorbedDays;
        AbsorbedIntoActivityCodes = input.AbsorbedIntoActivityCodes;
    }
}
