using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// Per-project configuration for the weather EOT evaluator (S11-BE-02, domain-rules.md weather-eot
/// §3.5), 1:1 with <see cref="Project"/>. Every field here has a defensible, documented default
/// (§12 Q2/Q3), so a project with no row at all is <b>not</b> a configuration error - the
/// Application layer (<c>EvaluateEotCommandHandler</c>) substitutes the same defaults named here
/// when <c>IEotEvaluationRepository.GetPolicyAsync</c> returns <see langword="null"/>. This entity
/// exists so a project CAN override them, not because every project must have one.
///
/// <para><b>Deliberately does not model <c>CountingBasis</c>/`ExceedanceOverBaseline` or
/// <c>ProjectEotMonthlyAllowance</c></b> (domain-rules.md §3.5's other half). ADR-0020
/// (human-confirmed 2026-08-10, i.e. after this document) ruled stoppage days are counted
/// <b>absolutely</b> "over exceedance and <i>over a per-project switch</i>" - so building a
/// configurable counting basis now would ship a switch the human explicitly declined. ADR-0020's own
/// consequences note this "does not foreclose" adding exceedance later "as an additional filter over
/// the same absolute base" - if/when that happens, it is a new field and reader, not a change to any
/// of the fields below.</para>
///
/// <para><b>Deliberately does not model the schema's optional <c>CalendarId</c> override either</b> -
/// every fixture in domain-rules.md §10 uses the project's <c>Calendar.IsDefault</c> row, and Q6
/// (per-activity/alternate calendars) is recorded as explicitly out of scope. The evaluator always
/// resolves the project's default calendar; scoped here as a deferred, documented simplification
/// rather than a half-wired field nothing reads.</para>
/// </summary>
public sealed class ProjectEotPolicy : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }

    public Guid ProjectId { get; private set; }

    public EotPartialDayPolicy PartialDayPolicy { get; private set; }

    /// <summary><c>decimal(4,2)</c>, hours in a full working shift. domain-rules.md §3.4 default 8.00.</summary>
    public decimal FullDayHours { get; private set; }

    /// <summary><c>decimal(4,2)</c>, only read under <see cref="EotPartialDayPolicy.ThresholdWholeDay"/>
    /// (§3.4: "read only under ThresholdWholeDay"). domain-rules.md §3.4 default 4.00.</summary>
    public decimal MinHoursLostForCountableDay { get; private set; }

    /// <summary><c>decimal(6,2)</c>. <see langword="null"/> = no depth test configured, never 0.00
    /// (ADR-0015 precedent, domain-rules.md §3.5).</summary>
    public decimal? MinRainfallMmForCountableDay { get; private set; }

    /// <summary>Fail-closed default <see langword="false"/> (§3.5): when a depth threshold is
    /// configured and <c>RainfallMm</c> is unmeasured, the day is not countable unless a tenant
    /// explicitly opts in via this flag.</summary>
    public bool CountUnmeasuredRainfallWhenThresholdSet { get; private set; }

    /// <summary><see langword="null"/> = not configured (§3.6); the evaluator omits the Notice
    /// fields entirely rather than computing against a guessed period.</summary>
    public int? NoticePeriodDays { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private ProjectEotPolicy()
    {
    }

    public ProjectEotPolicy(
        Guid tenantId,
        Guid projectId,
        EotPartialDayPolicy partialDayPolicy = EotPartialDayPolicy.ThresholdWholeDay,
        decimal fullDayHours = 8.00m,
        decimal minHoursLostForCountableDay = 4.00m,
        decimal? minRainfallMmForCountableDay = null,
        bool countUnmeasuredRainfallWhenThresholdSet = false,
        int? noticePeriodDays = null)
    {
        if (projectId == Guid.Empty)
        {
            throw new DomainException("ProjectEotPolicy.ProjectId is required.");
        }

        if (fullDayHours is <= 0 or > 24)
        {
            throw new DomainException("ProjectEotPolicy.FullDayHours must be between 0 (exclusive) and 24.");
        }

        if (minHoursLostForCountableDay is < 0 or > 24)
        {
            throw new DomainException("ProjectEotPolicy.MinHoursLostForCountableDay must be between 0 and 24.");
        }

        if (minRainfallMmForCountableDay is < 0)
        {
            throw new DomainException("ProjectEotPolicy.MinRainfallMmForCountableDay cannot be negative.");
        }

        if (noticePeriodDays is < 0)
        {
            throw new DomainException("ProjectEotPolicy.NoticePeriodDays cannot be negative.");
        }

        TenantId = tenantId;
        ProjectId = projectId;
        PartialDayPolicy = partialDayPolicy;
        FullDayHours = fullDayHours;
        MinHoursLostForCountableDay = minHoursLostForCountableDay;
        MinRainfallMmForCountableDay = minRainfallMmForCountableDay;
        CountUnmeasuredRainfallWhenThresholdSet = countUnmeasuredRainfallWhenThresholdSet;
        NoticePeriodDays = noticePeriodDays;
    }
}
