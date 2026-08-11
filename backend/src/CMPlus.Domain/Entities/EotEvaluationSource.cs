using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// One considered <see cref="DailyWeatherLog"/> entry's fate within an <see cref="EotEvaluation"/>
/// (domain-rules.md weather-eot §3: "the evaluator never silently drops a row"). Exactly one row per
/// in-force, in-window entry the evaluator looked at - <see cref="ExclusionReason"/> is
/// <see langword="null"/> when the entry contributed at least one countable day to at least one
/// named activity, and set to the first gate it failed otherwise (§3's gate order).
///
/// <para><see cref="IAppendOnly"/> - constructor is <c>internal</c>, reachable only through
/// <see cref="EotEvaluation.Capture"/>.</para>
/// </summary>
public sealed class EotEvaluationSource : Entity, ITenantOwned, IAppendOnly
{
    public Guid TenantId { get; private set; }

    public Guid EotEvaluationId { get; private set; }

    public Guid DailyWeatherLogId { get; private set; }

    /// <summary><c>decimal(5,2)</c>. The day-weight this entry contributed once past its entry-level
    /// gates (1.00 under <see cref="EotPartialDayPolicy.FullDayOnly"/>/<see cref="EotPartialDayPolicy.ThresholdWholeDay"/>,
    /// raw <c>HoursLost</c> under <see cref="EotPartialDayPolicy.FractionalAccrual"/> before the
    /// per-activity, per-run floor); 0 when <see cref="ExclusionReason"/> is set.</summary>
    public decimal CountableDays { get; private set; }

    public EotExclusionReason? ExclusionReason { get; private set; }

    /// <summary>domain-rules.md §3.4/§8.5: <see langword="true"/> when this entry's recorded
    /// <c>HoursLost</c> exceeded the policy's <c>FullDayHours</c> (H) and was charged as H instead -
    /// only ever possible under <see cref="Enums.EotPartialDayPolicy.FractionalAccrual"/> (the other
    /// two policies compare <c>HoursLost</c> against a threshold and never sum it, so the clamp never
    /// bites there - fixture W-18b). Disclosure only: the underlying <see cref="DailyWeatherLog"/> row
    /// still carries its true, un-clamped <c>HoursLost</c> forever (§3.4: "the clamp is an
    /// evaluation-time modelling step; it must never write back to the immutable log").</summary>
    public bool HoursLostClampedToFullDay { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private EotEvaluationSource()
    {
    }

    internal EotEvaluationSource(Guid tenantId, Guid eotEvaluationId, EotEvaluationSourceInput input)
    {
        if (eotEvaluationId == Guid.Empty)
        {
            throw new DomainException("EotEvaluationSource.EotEvaluationId is required.");
        }

        if (input.DailyWeatherLogId == Guid.Empty)
        {
            throw new DomainException("EotEvaluationSource.DailyWeatherLogId is required.");
        }

        if (input.CountableDays < 0)
        {
            throw new DomainException("EotEvaluationSource.CountableDays cannot be negative.");
        }

        TenantId = tenantId;
        EotEvaluationId = eotEvaluationId;
        DailyWeatherLogId = input.DailyWeatherLogId;
        CountableDays = input.CountableDays;
        ExclusionReason = input.ExclusionReason;
        HoursLostClampedToFullDay = input.HoursLostClampedToFullDay;
    }
}
