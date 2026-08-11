using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// One append-only snapshot of a full CPM recalculation (ADR-0019, domain-rules.md weather-eot
/// §4.3) - captured every time <c>RecalculateCpmCommand</c> succeeds, alongside (never instead of)
/// the live <see cref="Activity.IsCritical"/>/<see cref="Activity.TotalFloat"/>/
/// <see cref="Activity.FreeFloat"/> write-back those three fields still receive. Exists because
/// those three <see cref="Activity"/> fields are overwritten on every recalculation with no
/// history, so "was activity X critical on date d" is unanswerable against the live schema alone -
/// the not-yet-built EotEvaluator (S11-BE-02) needs the schedule <i>as it stood on the stoppage
/// date</i> (the contemporaneous reading, domain-rules.md §4.1/§4.2), which means re-running
/// <see cref="CMPlus.Application.Services.Cpm.CpmEngine"/> against the network as it existed then -
/// hence <see cref="CpmRunRelation"/> is captured too, not just per-activity floats
/// (domain-rules.md §4.3: "not optional").
///
/// <para>The governing run for a stoppage on date $d$ is
/// $\arg\max\{r.\mathrm{CalculatedAt} \le d\}$ (domain-rules.md §4.1) - see
/// <c>ICpmRunHistoryReader.GetGoverningRunAsync</c>, the read side of this history. Selecting and
/// interpreting the governing run (the §4.4 degraded-mode Confidence/CriticalityBasis rules, the
/// §5 windows Time Impact Analysis) is entirely S11-BE-02's job; this type only captures and
/// stores the network faithfully.</para>
///
/// <para><b>Append-only by construction and by marker.</b> No mutator method or public setter
/// exists anywhere on this aggregate or its two child collections - the same structural discipline
/// <see cref="DailyWeatherLog"/> established, applied here from the start rather than retrofitted
/// (unlike <see cref="ActivityProgressLog"/>/<c>ApprovalAction</c>, both of which shipped without
/// <see cref="IAppendOnly"/> first and were only fixed after a raw-<c>DbContext</c> rewrite was
/// execution-verified - see
/// <see cref="Infrastructure.Persistence.Interceptors.AppendOnlyGuardInterceptor"/>'s own remarks).
/// <see cref="IAppendOnly"/> is what makes the guarantee structural rather than a convention nobody
/// enforces.</para>
///
/// <para><b>Retention is deliberately not built here (ADR-0019 consequences).</b> A future pruning
/// job must never delete a run an <c>EotEvaluation</c> cites; nothing here forecloses that - a
/// <see cref="Entity.Id"/> is a stable, referenceable key, no delete path is wired anywhere (not
/// even cascade from a child - see the owned-collection configuration), and any real pruning would
/// necessarily be a deliberate maintenance operation outside the ordinary write path, exactly like
/// this marker already assumes for every other <see cref="IAppendOnly"/> row.</para>
/// </summary>
public sealed class CpmRun : Entity, ITenantOwned, IAppendOnly
{
    private readonly List<CpmRunActivity> _activities = [];
    private readonly List<CpmRunRelation> _relations = [];

    public Guid TenantId { get; private set; }

    public Guid ProjectId { get; private set; }

    public DateTimeOffset CalculatedAt { get; private set; }

    /// <summary>The project's <c>DataDate</c> at the moment of this recalculation, purely as
    /// descriptive metadata - the shipped <c>CpmEngine</c> is day-count/calendar-agnostic (its own
    /// remarks) and never reads a data date at all, so this is never an input to
    /// <see cref="ProjectDurationDays"/> or any child row, only a record of what the project's data
    /// date nominally was at capture time. Null is legitimate - never defaulted (same discipline as
    /// <see cref="DailyWeatherLog.RainfallMm"/>).</summary>
    public DateTimeOffset? DataDate { get; private set; }

    public int ProjectDurationDays { get; private set; }

    /// <summary>Null only for a <see cref="CpmRunTrigger.System"/> run with no human actor -
    /// nullable rather than a fabricated placeholder id, mirroring <see cref="AuditLog.UserId"/>'s
    /// established "null = no human actor" convention. Every wired caller today
    /// (<c>RecalculateCpmCommandHandler</c>, <see cref="CpmRunTrigger.Manual"/> only) fails closed
    /// instead of ever constructing a run with a null actor (L-01 discipline, mirrors
    /// <c>VariationOrder</c>/<c>DailyWeatherLog</c>) - see that handler's remarks. domain-rules.md
    /// §4.3's own field list does not mark this NULL the way it marks <see cref="DataDate"/>, so
    /// this nullability is this task's own interpretation, made for consistency with the rest of
    /// the codebase's "never fabricate an actor" rule now that <see cref="CpmRunTrigger.System"/>
    /// exists as a value with no human actor to attribute - flagged in the handoff report rather
    /// than silently assumed.</summary>
    public Guid? TriggeredByUserId { get; private set; }

    public CpmRunTrigger Trigger { get; private set; }

    public IReadOnlyList<CpmRunActivity> Activities => _activities.AsReadOnly();

    public IReadOnlyList<CpmRunRelation> Relations => _relations.AsReadOnly();

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private CpmRun()
    {
    }

    private CpmRun(
        Guid tenantId,
        Guid projectId,
        DateTimeOffset calculatedAt,
        DateTimeOffset? dataDate,
        int projectDurationDays,
        Guid? triggeredByUserId,
        CpmRunTrigger trigger,
        IReadOnlyCollection<CpmRunActivityInput> activities,
        IReadOnlyCollection<CpmRunRelationInput> relations)
    {
        if (projectId == Guid.Empty)
        {
            throw new DomainException("CpmRun.ProjectId is required.");
        }

        if (projectDurationDays < 0)
        {
            throw new DomainException("CpmRun.ProjectDurationDays cannot be negative.");
        }

        TenantId = tenantId;
        ProjectId = projectId;
        CalculatedAt = calculatedAt;
        DataDate = dataDate;
        ProjectDurationDays = projectDurationDays;
        TriggeredByUserId = triggeredByUserId;
        Trigger = trigger;

        foreach (var activity in activities)
        {
            _activities.Add(new CpmRunActivity(tenantId, Id, activity));
        }

        foreach (var relation in relations)
        {
            _relations.Add(new CpmRunRelation(tenantId, Id, relation));
        }
    }

    /// <summary>The only way to obtain an instance - one snapshot of a whole, successful
    /// <c>CpmEngine.Calculate</c> result (domain-rules.md §4.3). A rejected graph (cycle/duplicate/
    /// unknown-activity) must never reach here - <c>RecalculateCpmCommandHandler</c> returns before
    /// building one, the same all-or-nothing discipline that already governs the Activity
    /// write-back. Empty <paramref name="activities"/>/<paramref name="relations"/> are legitimate
    /// (a project with zero activities yet still recalculates to a valid, empty run).</summary>
    public static CpmRun Capture(
        Guid tenantId,
        Guid projectId,
        DateTimeOffset calculatedAt,
        DateTimeOffset? dataDate,
        int projectDurationDays,
        Guid? triggeredByUserId,
        CpmRunTrigger trigger,
        IReadOnlyCollection<CpmRunActivityInput> activities,
        IReadOnlyCollection<CpmRunRelationInput> relations) =>
        new(tenantId, projectId, calculatedAt, dataDate, projectDurationDays, triggeredByUserId, trigger, activities, relations);
}

/// <summary>Plain data shape for one activity's result supplied to <see cref="CpmRun.Capture"/> -
/// mirrors <see cref="VariationOrderScopeItemInput"/>'s "input shape before it becomes a child
/// entity" pattern. Field set matches <c>CpmActivityResult</c> plus the duration the engine was
/// given (<c>CpmActivityResult</c> itself does not carry duration back).</summary>
public sealed record CpmRunActivityInput(
    Guid ActivityId,
    int DurationDays,
    int EarlyStart,
    int EarlyFinish,
    int LateStart,
    int LateFinish,
    int TotalFloat,
    int FreeFloat,
    bool IsCritical);

/// <summary>Plain data shape for one relation supplied to <see cref="CpmRun.Capture"/> - mirrors
/// <see cref="ActivityRelation"/>'s own four fields exactly (the run's network topology,
/// domain-rules.md §4.3: "not optional").</summary>
public sealed record CpmRunRelationInput(
    Guid PredecessorActivityId, Guid SuccessorActivityId, RelationType RelationType, int LagDays);
