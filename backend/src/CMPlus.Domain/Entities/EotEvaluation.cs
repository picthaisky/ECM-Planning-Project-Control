using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// One immutable, dated, reproducible snapshot of an <c>EotEvaluator</c> run (S11-BE-02,
/// domain-rules.md weather-eot §8.5). <see cref="IAppendOnly"/> for the same reason
/// <see cref="DailyWeatherLog"/>/<see cref="CpmRun"/> are: a correction never mutates a stored
/// evaluation (§8.5 rule 1) - a correction to the underlying weather log or a later CPM
/// recalculation makes THIS row <c>Stale</c> (a read-time computation, not a stored flag - §8.5 rule
/// 2), and the remedy is always a brand new <see cref="EotEvaluation"/>, never an edit.
///
/// <para><b>The zero-side-effect boundary (§2.1, fixture W-14) is structural, not a promise.</b> This
/// type, its three child collections, and one <c>AuditLog</c> row are the only writes
/// <c>EvaluateEotCommandHandler</c>/<c>IEotEvaluationRepository.SaveAsync</c> ever perform - there is
/// no code path anywhere in this aggregate that could touch <see cref="Activity.IsCritical"/>/
/// <see cref="Activity.TotalFloat"/>/<see cref="Activity.FreeFloat"/>, move a date, or trigger a CPM
/// recalculation, because nothing in this type references <see cref="Activity"/> for write access at
/// all (<see cref="EotEvaluationDriver.ActivityId"/> is read-only evidence, exactly like
/// <see cref="DailyWeatherLogActivity.ActivityId"/>).</para>
///
/// <para><b><see cref="ConcurrencyAssessed"/>/<see cref="EntitlementBasisAssessed"/> are hard-coded
/// <see langword="false"/> here, not caller-supplied parameters</b> (§2.2, §7.2: concurrent delay is
/// explicitly out of scope for Sprint 11) - the same "not a settable constructor parameter" discipline
/// <see cref="Project.OriginalBac"/> uses, so no caller can ever construct a row that claims either
/// was assessed.</para>
/// </summary>
public sealed class EotEvaluation : Entity, ITenantOwned, IAppendOnly
{
    private readonly List<EotEvaluationRun> _runs = [];
    private readonly List<EotEvaluationSource> _sources = [];
    private readonly List<EotEvaluationDriver> _drivers = [];

    public Guid TenantId { get; private set; }

    public Guid ProjectId { get; private set; }

    public DateTimeOffset WindowStart { get; private set; }

    public DateTimeOffset WindowEnd { get; private set; }

    public DateTimeOffset EvaluatedAt { get; private set; }

    public Guid EvaluatedByUserId { get; private set; }

    public EotCriticalityBasis CriticalityBasis { get; private set; }

    public EotConfidence Confidence { get; private set; }

    /// <summary>Sum, over every <see cref="EotEvaluationRun"/> this evaluation produced, of that
    /// run's own as-scheduled project duration (§5.1's $D^{as}_r$). Reconciling summary, not a
    /// literal single project-duration figure when more than one governing run is involved (W-15) -
    /// the per-run truth is <see cref="Runs"/>. Provably satisfies
    /// <c>ImpactedDurationDays - AsScheduledDurationDays == EotEligibleDays</c> always: see this
    /// type's own remarks on <see cref="ImpactedDurationDays"/>.</summary>
    public int AsScheduledDurationDays { get; private set; }

    /// <summary>Sum, over every <see cref="EotEvaluationRun"/>, of that run's own impacted project
    /// duration ($D^{imp}_r$). Every run that contributes at least one countable day has
    /// $D^{imp}_r \ge D^{as}_r$ by construction (§5.1: "adding non-negative durations to a max-plus
    /// network can never shorten it"), so summing both sides and subtracting reproduces
    /// <see cref="EotEligibleDays"/> exactly - not a coincidence, a property of the algorithm.</summary>
    public int ImpactedDurationDays { get; private set; }

    /// <summary><c>E</c> - domain-rules.md §2.2: a schedule fact, not an entitlement. Named exactly
    /// as the domain document requires so no DTO/UI can present it as a contractual right.</summary>
    public int EotEligibleDays { get; private set; }

    /// <summary>Sum of countable (activity, day) charges actually applied across every activity and
    /// governing run (i.e. $\sum_r \sum_j S_j^{(r)}$, post the §3.7 per-activity window gate and
    /// §5.2a's serial-chain collapse) - raw evidence volume, deliberately distinct from
    /// <see cref="DistinctCountableDateCount"/> (the §5.3 cap's denominator) and from
    /// <see cref="EotEligibleDays"/> (which nets against float).</summary>
    public int CountableStoppageDayCount { get; private set; }

    /// <summary>domain-rules.md §5.2a/§8.5: days that passed every §3 gate but the serial-chain
    /// collapse absorbed into a serially-chained activity on the same governing run, rather than
    /// charging the same lost day into more than one point of a common CPM path.
    /// <see cref="CountableStoppageDayCount"/> + <see cref="SerialChainAbsorbedDayCount"/> is the
    /// number of (activity, day) stoppages that cleared all six §3 gates - the figure that must
    /// reconcile against the weather log when a reviewer audits the evaluation, not
    /// <see cref="CountableStoppageDayCount"/> alone. <c>0</c> for every domain-rules.md fixture
    /// except W-17 and W-20 (every other fixture charges at most one activity per date).</summary>
    public int SerialChainAbsorbedDayCount { get; private set; }

    /// <summary>§5.3's cap denominator: count of distinct calendar dates that were countable for at
    /// least one activity. <c>EotEligibleDays &lt;= DistinctCountableDateCount</c> always (the
    /// "one calendar day never yields more than one EOT day" invariant) - asserted by
    /// <c>EotEvaluatorTests</c> on every fixture, not just trusted.</summary>
    public int DistinctCountableDateCount { get; private set; }

    /// <summary>§3.2: count of weather log entries excluded for naming no activity at all. Not
    /// distributed across in-progress activities (§3.2's explicit negative rule) and contributes 0
    /// to <see cref="EotEligibleDays"/>.</summary>
    public int UnattributedStoppageDayCount { get; private set; }

    /// <summary>Always <see langword="false"/> for every Sprint 11 evaluation (§7.2 ruling) - see
    /// this type's class remarks.</summary>
    public bool ConcurrencyAssessed { get; private set; }

    /// <summary>Always <see langword="false"/> - §2.2: entitlement additionally needs the weather to
    /// qualify as exceptional/เหตุสุดวิสัย and timely notice, neither of which this evaluator judges.</summary>
    public bool EntitlementBasisAssessed { get; private set; }

    /// <summary>Every <see cref="ProjectEotPolicy"/> value actually used, pinned as JSON (mirrors
    /// <c>ApprovalPolicyVersion</c>'s pinning rationale, §8.5) - a two-year-old figure must remain
    /// explainable even after the project's thresholds are later changed.</summary>
    public string PolicySnapshotJson { get; private set; } = string.Empty;

    /// <summary>§3.6, advisory only, zero arithmetic effect on <see cref="EotEligibleDays"/>.
    /// <see langword="null"/> when <c>NoticePeriodDays</c> is not configured, or when there is no
    /// countable date to measure from - omitted entirely, never a guessed value.</summary>
    public DateTimeOffset? LatestNoticeDate { get; private set; }

    public bool? NoticeWindowExpired { get; private set; }

    public IReadOnlyList<EotEvaluationRun> Runs => _runs.AsReadOnly();

    public IReadOnlyList<EotEvaluationSource> Sources => _sources.AsReadOnly();

    public IReadOnlyList<EotEvaluationDriver> Drivers => _drivers.AsReadOnly();

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private EotEvaluation()
    {
    }

    private EotEvaluation(
        Guid tenantId,
        Guid projectId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        DateTimeOffset evaluatedAt,
        Guid evaluatedByUserId,
        EotCriticalityBasis criticalityBasis,
        EotConfidence confidence,
        int asScheduledDurationDays,
        int impactedDurationDays,
        int eotEligibleDays,
        int countableStoppageDayCount,
        int serialChainAbsorbedDayCount,
        int distinctCountableDateCount,
        int unattributedStoppageDayCount,
        string policySnapshotJson,
        DateTimeOffset? latestNoticeDate,
        bool? noticeWindowExpired,
        IReadOnlyCollection<EotEvaluationRunInput> runs,
        IReadOnlyCollection<EotEvaluationSourceInput> sources,
        IReadOnlyCollection<EotEvaluationDriverInput> drivers)
    {
        if (projectId == Guid.Empty)
        {
            throw new DomainException("EotEvaluation.ProjectId is required.");
        }

        if (evaluatedByUserId == Guid.Empty)
        {
            throw new DomainException("EotEvaluation.EvaluatedByUserId is required.");
        }

        if (windowEnd < windowStart)
        {
            throw new DomainException("EotEvaluation.WindowEnd cannot precede WindowStart.");
        }

        if (asScheduledDurationDays < 0 || impactedDurationDays < 0 || eotEligibleDays < 0
            || countableStoppageDayCount < 0 || serialChainAbsorbedDayCount < 0
            || distinctCountableDateCount < 0 || unattributedStoppageDayCount < 0)
        {
            throw new DomainException("EotEvaluation duration/count fields cannot be negative.");
        }

        TenantId = tenantId;
        ProjectId = projectId;
        WindowStart = windowStart;
        WindowEnd = windowEnd;
        EvaluatedAt = evaluatedAt;
        EvaluatedByUserId = evaluatedByUserId;
        CriticalityBasis = criticalityBasis;
        Confidence = confidence;
        AsScheduledDurationDays = asScheduledDurationDays;
        ImpactedDurationDays = impactedDurationDays;
        EotEligibleDays = eotEligibleDays;
        CountableStoppageDayCount = countableStoppageDayCount;
        SerialChainAbsorbedDayCount = serialChainAbsorbedDayCount;
        DistinctCountableDateCount = distinctCountableDateCount;
        UnattributedStoppageDayCount = unattributedStoppageDayCount;
        ConcurrencyAssessed = false;
        EntitlementBasisAssessed = false;
        PolicySnapshotJson = policySnapshotJson;
        LatestNoticeDate = latestNoticeDate;
        NoticeWindowExpired = noticeWindowExpired;

        foreach (var run in runs)
        {
            _runs.Add(new EotEvaluationRun(tenantId, Id, run));
        }

        foreach (var source in sources)
        {
            _sources.Add(new EotEvaluationSource(tenantId, Id, source));
        }

        foreach (var driver in drivers)
        {
            _drivers.Add(new EotEvaluationDriver(tenantId, Id, driver));
        }
    }

    /// <summary>The only way to obtain an instance - one whole, successful <c>EotEvaluator.Evaluate</c>
    /// result. Mirrors <see cref="CpmRun.Capture"/>'s "capture the whole outcome atomically" shape.</summary>
    public static EotEvaluation Capture(
        Guid tenantId,
        Guid projectId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        DateTimeOffset evaluatedAt,
        Guid evaluatedByUserId,
        EotCriticalityBasis criticalityBasis,
        EotConfidence confidence,
        int asScheduledDurationDays,
        int impactedDurationDays,
        int eotEligibleDays,
        int countableStoppageDayCount,
        int serialChainAbsorbedDayCount,
        int distinctCountableDateCount,
        int unattributedStoppageDayCount,
        string policySnapshotJson,
        DateTimeOffset? latestNoticeDate,
        bool? noticeWindowExpired,
        IReadOnlyCollection<EotEvaluationRunInput> runs,
        IReadOnlyCollection<EotEvaluationSourceInput> sources,
        IReadOnlyCollection<EotEvaluationDriverInput> drivers) =>
        new(
            tenantId, projectId, windowStart, windowEnd, evaluatedAt, evaluatedByUserId, criticalityBasis, confidence,
            asScheduledDurationDays, impactedDurationDays, eotEligibleDays, countableStoppageDayCount,
            serialChainAbsorbedDayCount, distinctCountableDateCount, unattributedStoppageDayCount, policySnapshotJson,
            latestNoticeDate, noticeWindowExpired, runs, sources, drivers);
}

/// <summary>Plain data shape for one <see cref="EotEvaluationRun"/> supplied to
/// <see cref="EotEvaluation.Capture"/> - one row per governing <see cref="CpmRun"/> the evaluation
/// actually charged countable days against (domain-rules.md §5.1).</summary>
public sealed record EotEvaluationRunInput(
    Guid CpmRunId,
    DateTimeOffset WindowFrom,
    DateTimeOffset WindowTo,
    int AsScheduledDurationDays,
    int ImpactedDurationDays,
    int DeltaDays);

/// <summary>Plain data shape for one <see cref="EotEvaluationSource"/> supplied to
/// <see cref="EotEvaluation.Capture"/> - one row per considered <see cref="DailyWeatherLog"/> entry
/// (domain-rules.md §3: "the evaluator never silently drops a row"). <see cref="HoursLostClampedToFullDay"/>
/// is §3.4's H1 disclosure flag - see <see cref="EotEvaluationSource"/>'s own remarks.</summary>
public sealed record EotEvaluationSourceInput(
    Guid DailyWeatherLogId,
    decimal CountableDays,
    EotExclusionReason? ExclusionReason,
    bool HoursLostClampedToFullDay = false);

/// <summary>Plain data shape for one <see cref="EotEvaluationDriver"/> supplied to
/// <see cref="EotEvaluation.Capture"/> - domain-rules.md §5.4's explainability row, one per
/// (governing run, activity) pair that had at least one countable day charged to it <b>or</b> absorbed
/// away from it by §5.2a's serial-chain collapse (<see cref="SerialChainAbsorbedDays"/> &gt; 0 with
/// <see cref="StoppageDays"/> = 0 is a legitimate, expected row shape - W-17's C, W-20's A).
/// <see cref="CpmRunId"/> (not a freshly-generated <c>EotEvaluationRunId</c>) is the correlation key
/// back to the matching <see cref="EotEvaluationRun"/> row, because both are known before either
/// child collection is constructed - see the S11-BE-02 backend-developer report for why an
/// <see cref="Entity.Id"/>-based cross-reference does not work here (ids are assigned inside each
/// entity's own constructor, so nothing outside can know a sibling's id in advance).</summary>
public sealed record EotEvaluationDriverInput(
    Guid CpmRunId,
    Guid ActivityId,
    string ActivityCode,
    string ActivityName,
    int StoppageDays,
    int TotalFloatAtRun,
    bool WasCriticalAtRun,
    bool IsOnImpactedCriticalPath,
    int IndicativeEotDays,
    int MarginalEotDays,
    int RemainingFloatAfter,
    decimal? UnclaimedFractionalHours,
    int SerialChainAbsorbedDays = 0,
    string? AbsorbedIntoActivityCodes = null);
