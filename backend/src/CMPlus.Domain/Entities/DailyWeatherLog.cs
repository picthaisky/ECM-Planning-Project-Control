using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// Daily site weather observation (S11-BE-01, US-11.1) - tamper-evident legal evidence backing EOT
/// (extension-of-time) claims. Shape and correction semantics per
/// <c>docs/specs/weather-eot/domain-rules.md</c> §8 (the authoritative domain artifact this task
/// was told to await before finalising anything beyond a plain immutable log + nullable
/// self-reference - it landed mid-implementation and this type was reconciled against it).
///
/// <para><b>Immutable by construction.</b> <see cref="IAppendOnly"/> is the same structural
/// guarantee <see cref="ApprovalAction"/>/<see cref="ActualCostEntry"/> already use (security
/// review sprint-09.md M-01) - <see cref="Infrastructure.Persistence.Interceptors.AppendOnlyGuardInterceptor"/>
/// throws if any code path ever tries to <c>Modified</c>/<c>Deleted</c> this row. There is no
/// mutator method anywhere on this type - a correction is always a new row via
/// <see cref="CreateCorrection"/>/<see cref="CreateRetraction"/>, never an edit
/// (domain-rules.md §8.3's three-layer defence: no route, no mutator, the interceptor).</para>
///
/// <para><b>Supersession is a forward pointer only (§8.2) - never stamped on the original.</b>
/// <see cref="CorrectsWeatherLogId"/> points backward from a <see cref="WeatherLogEntryKind.Correction"/>/
/// <see cref="WeatherLogEntryKind.Retraction"/> row to the row it replaces/voids; the original is
/// never touched (no <c>SupersededBy</c> column exists - see §8.2's own rejection of that design).
/// The effective log set is computed at read/evaluation time, not maintained as a stored flag:
/// an entry is "in force" iff nothing points at it and it is not itself a <see cref="WeatherLogEntryKind.Retraction"/>.
/// A <see cref="WeatherLogEntryKind.Retraction"/> removes both itself and its target from that set.
/// Chain integrity (at most one entry may point at a given entry; a correction must target the
/// current chain tail; the target must be older) is enforced at the Application layer, where a DB
/// read is possible - see <c>RecordWeatherLogCorrectionCommandHandler</c>.</para>
///
/// <para><b>Deliberately project-scoped, not activity-scoped</b> - see <see cref="AffectedActivities"/>'s
/// own remarks: which activities were tagged is data capture, not an EOT determination, which
/// belongs entirely to the not-yet-built <c>EotEvaluator</c> (S11-BE-02, out of this task's scope).</para>
/// </summary>
public sealed class DailyWeatherLog : Entity, ITenantOwned, IAppendOnly
{
    private readonly List<DailyWeatherLogActivity> _affectedActivities = [];

    public Guid TenantId { get; private set; }

    public Guid ProjectId { get; private set; }

    /// <summary>The calendar day being observed (site-local); may differ from <see cref="RecordedAt"/>
    /// when logged after the fact (e.g. a next-morning catch-up entry, or a dated correction).</summary>
    public DateTimeOffset LogDate { get; private set; }

    public WeatherCondition Condition { get; private set; }

    /// <summary>Free-text detail alongside the closed <see cref="Condition"/> vocabulary.</summary>
    public string? ConditionNote { get; private set; }

    /// <summary>24-hour depth at the site, <c>decimal(6,2)</c>. Null means genuinely not measured -
    /// never defaulted to 0 (domain-rules.md §8.1: "NULL = not measured, never 0", mirrors
    /// <c>ActivityProgressLog.ActualQuantity</c>'s identical discipline).</summary>
    public decimal? RainfallMm { get; private set; }

    public WeatherImpact Impact { get; private set; }

    /// <summary>Free-text impact narrative ("หยุดเทคอนกรีตโซน B ครึ่งวัน").</summary>
    public string? ImpactNote { get; private set; }

    /// <summary>Working hours lost, <c>decimal(4,2)</c>, <c>0-24</c>. Required (both here and by
    /// <c>RecordWeatherLogCommandValidator</c>) whenever <see cref="Impact"/> is not
    /// <see cref="WeatherImpact.NoImpact"/> - domain-rules.md §3.4.</summary>
    public decimal? HoursLost { get; private set; }

    /// <summary>Derived, never independently stored - domain-rules.md §3.4/§8.1, the same
    /// discipline <c>VariationOrder.Type</c> uses for the sign of <c>Amount</c>.</summary>
    public bool WorkStoppage => Impact != WeatherImpact.NoImpact;

    public Guid RecordedByUserId { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public WeatherLogEntryKind EntryKind { get; private set; }

    /// <summary>Required iff <see cref="EntryKind"/> is not <see cref="WeatherLogEntryKind.Original"/>
    /// (domain-rules.md §8.1/§8.2). Existence-in-the-same-project and chain-tail validation happen
    /// at the Application layer (a constructor cannot see other rows).</summary>
    public Guid? CorrectsWeatherLogId { get; private set; }

    /// <summary>Required iff <see cref="EntryKind"/> is not <see cref="WeatherLogEntryKind.Original"/>
    /// (domain-rules.md §8.1 rule 5, Q8's ruling: no countersignature, but a reason is mandatory).</summary>
    public string? CorrectionReason { get; private set; }

    /// <summary>See this type's class remarks - data capture only, not an EOT determination.</summary>
    public IReadOnlyList<DailyWeatherLogActivity> AffectedActivities => _affectedActivities.AsReadOnly();

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private DailyWeatherLog()
    {
    }

    private DailyWeatherLog(
        Guid tenantId,
        Guid projectId,
        DateTimeOffset logDate,
        WeatherCondition condition,
        string? conditionNote,
        decimal? rainfallMm,
        WeatherImpact impact,
        string? impactNote,
        decimal? hoursLost,
        Guid recordedByUserId,
        DateTimeOffset recordedAt,
        WeatherLogEntryKind entryKind,
        Guid? correctsWeatherLogId,
        string? correctionReason,
        IReadOnlyCollection<Guid> affectedActivityIds)
    {
        if (projectId == Guid.Empty)
        {
            throw new DomainException("DailyWeatherLog.ProjectId is required.");
        }

        if (recordedByUserId == Guid.Empty)
        {
            throw new DomainException("DailyWeatherLog.RecordedByUserId is required.");
        }

        if (rainfallMm is < 0m)
        {
            throw new DomainException("DailyWeatherLog.RainfallMm cannot be negative.");
        }

        if (rainfallMm is { } rainfall && rainfall != Math.Round(rainfall, 2, MidpointRounding.AwayFromZero))
        {
            throw new DomainException("DailyWeatherLog.RainfallMm cannot have more than 2 decimal places.");
        }

        if (hoursLost is < 0m or > 24m)
        {
            throw new DomainException("DailyWeatherLog.HoursLost must be between 0 and 24.");
        }

        // domain-rules.md §3.4: "HoursLost is required by FluentValidation when Impact <> NoImpact" -
        // enforced here too (belt-and-braces domain invariant, same discipline as every other
        // cross-field business rule in this codebase).
        if (impact != WeatherImpact.NoImpact && hoursLost is null)
        {
            throw new DomainException("DailyWeatherLog.HoursLost is required when Impact is not NoImpact.");
        }

        if (entryKind == WeatherLogEntryKind.Original)
        {
            if (correctsWeatherLogId is not null)
            {
                throw new DomainException("An Original weather log entry must not carry CorrectsWeatherLogId.");
            }

            if (correctionReason is not null)
            {
                throw new DomainException("An Original weather log entry must not carry CorrectionReason.");
            }
        }
        else
        {
            if (correctsWeatherLogId is null || correctsWeatherLogId.Value == Guid.Empty)
            {
                throw new DomainException($"DailyWeatherLog.CorrectsWeatherLogId is required for a {entryKind} entry.");
            }

            if (string.IsNullOrWhiteSpace(correctionReason))
            {
                throw new DomainException($"DailyWeatherLog.CorrectionReason is required for a {entryKind} entry.");
            }
        }

        TenantId = tenantId;
        ProjectId = projectId;
        LogDate = logDate;
        Condition = condition;
        ConditionNote = conditionNote;
        RainfallMm = rainfallMm;
        Impact = impact;
        ImpactNote = impactNote;
        HoursLost = hoursLost;
        RecordedByUserId = recordedByUserId;
        RecordedAt = recordedAt;
        EntryKind = entryKind;
        CorrectsWeatherLogId = correctsWeatherLogId;
        CorrectionReason = correctionReason;

        foreach (var activityId in affectedActivityIds)
        {
            _affectedActivities.Add(new DailyWeatherLogActivity(tenantId, Id, activityId));
        }
    }

    /// <summary>The only way to create a first-time entry - <see cref="EntryKind"/> is always
    /// <see cref="WeatherLogEntryKind.Original"/>, <see cref="CorrectsWeatherLogId"/>/
    /// <see cref="CorrectionReason"/> are always null.</summary>
    public static DailyWeatherLog CreateOriginal(
        Guid tenantId,
        Guid projectId,
        DateTimeOffset logDate,
        WeatherCondition condition,
        string? conditionNote,
        decimal? rainfallMm,
        WeatherImpact impact,
        string? impactNote,
        decimal? hoursLost,
        Guid recordedByUserId,
        DateTimeOffset recordedAt,
        IReadOnlyCollection<Guid> affectedActivityIds) =>
        new(
            tenantId, projectId, logDate, condition, conditionNote, rainfallMm, impact, impactNote, hoursLost,
            recordedByUserId, recordedAt, WeatherLogEntryKind.Original, correctsWeatherLogId: null,
            correctionReason: null, affectedActivityIds);

    /// <summary>A replacement entry - "there was weather, but the true figures are these" (domain-rules.md
    /// §8.2). <paramref name="correctsWeatherLogId"/>/<paramref name="correctionReason"/> are
    /// mandatory. The correction's own values govern completely (rule 6: "It replaces; it does not
    /// patch").</summary>
    public static DailyWeatherLog CreateCorrection(
        Guid tenantId,
        Guid projectId,
        Guid correctsWeatherLogId,
        string correctionReason,
        DateTimeOffset logDate,
        WeatherCondition condition,
        string? conditionNote,
        decimal? rainfallMm,
        WeatherImpact impact,
        string? impactNote,
        decimal? hoursLost,
        Guid recordedByUserId,
        DateTimeOffset recordedAt,
        IReadOnlyCollection<Guid> affectedActivityIds) =>
        new(
            tenantId, projectId, logDate, condition, conditionNote, rainfallMm, impact, impactNote, hoursLost,
            recordedByUserId, recordedAt, WeatherLogEntryKind.Correction, correctsWeatherLogId, correctionReason,
            affectedActivityIds);

    /// <summary>Voids the target entirely - "this entry should never have existed" (domain-rules.md
    /// §8.2). Removes both itself and its target from the effective log set. Carries the same full
    /// field set as a <see cref="CreateCorrection"/> row (the schema draws no distinction) even
    /// though its content is moot once retracted - the values are recorded for the audit trail
    /// only.</summary>
    public static DailyWeatherLog CreateRetraction(
        Guid tenantId,
        Guid projectId,
        Guid correctsWeatherLogId,
        string correctionReason,
        DateTimeOffset logDate,
        WeatherCondition condition,
        string? conditionNote,
        decimal? rainfallMm,
        WeatherImpact impact,
        string? impactNote,
        decimal? hoursLost,
        Guid recordedByUserId,
        DateTimeOffset recordedAt,
        IReadOnlyCollection<Guid> affectedActivityIds) =>
        new(
            tenantId, projectId, logDate, condition, conditionNote, rainfallMm, impact, impactNote, hoursLost,
            recordedByUserId, recordedAt, WeatherLogEntryKind.Retraction, correctsWeatherLogId, correctionReason,
            affectedActivityIds);
}
