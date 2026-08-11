using CMPlus.Application.Services.Cpm;
using CMPlus.Application.Services.Eot;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Eot;

/// <summary>
/// Shared setup for the domain-rules.md weather-eot §10.0 fixtures ("Network N1", calendar
/// "TH-6Day", default policy) - every <c>W-xx</c> test in <c>EotEvaluatorTests</c> builds on this so
/// no fixture hand-transcribes a CPM result table (every <see cref="CpmRun"/> here is built by
/// actually running <see cref="CpmEngine.Calculate"/>, the same way <c>RecalculateCpmCommandHandler</c>
/// does in production, never by copying numbers out of the domain document).
/// </summary>
public static class EotFixtures
{
    public static readonly Guid TenantId = Guid.NewGuid();
    public static readonly Guid ProjectId = Guid.NewGuid();
    public static readonly Guid ActorUserId = Guid.NewGuid();

    public static readonly Guid AId = Guid.NewGuid();
    public static readonly Guid BId = Guid.NewGuid();
    public static readonly Guid CId = Guid.NewGuid();
    public static readonly Guid DId = Guid.NewGuid();

    /// <summary>Midnight Thai time on the given calendar date - used consistently for every date
    /// value across a test (LogDate, CalculatedAt, Actual/PlannedStart/Finish, window bounds) so
    /// <c>DateOnly.FromDateTime(x.DateTime)</c> extraction is unambiguous (mirrors
    /// <c>WorkingCalendar</c>'s own "calendar-day identity, not an instant" convention).</summary>
    public static DateTimeOffset D(int year, int month, int day) => new(year, month, day, 0, 0, 0, TimeSpan.FromHours(7));

    public static readonly DateTimeOffset WindowStart = D(2026, 7, 1);
    public static readonly DateTimeOffset WindowEnd = D(2026, 7, 31);
    public static readonly DateTimeOffset DefaultEvaluatedAt = D(2026, 8, 1);

    /// <summary>"Mon-Sat" - domain-rules.md §10.0's calendar. Thai construction commonly runs a
    /// 6-day week; hard-coding Sat/Sun as universally non-working is exactly the defect §3.3 warns
    /// against.</summary>
    public static readonly WorkingDays Th6Day = WorkingDays.Weekdays | WorkingDays.Saturday;

    public static readonly IReadOnlyList<CpmRelationInput> N1Relations =
    [
        new(AId, BId, RelationType.FS, 0),
        new(AId, CId, RelationType.FS, 0),
        new(BId, DId, RelationType.FS, 0),
        new(CId, DId, RelationType.FS, 0),
    ];

    /// <summary>One <see cref="CalendarException"/>: 2026-07-28, non-working
    /// ("วันเฉลิมพระชนมพรรษา") - §10.0's shared setup.</summary>
    public static IReadOnlyList<CalendarException> Th6DayExceptions()
    {
        var calendar = new Calendar(TenantId, ProjectId, "TH-6Day", Th6Day, isDefault: true);
        return [calendar.AddException(D(2026, 7, 28).DateTime, isWorkingDay: false, "วันเฉลิมพระชนมพรรษา")];
    }

    public static EotCalendarContext Th6DayCalendar() => new(Th6Day, Th6DayExceptions());

    /// <summary>§10.0: "All activities in progress (ActualStart set, ActualFinish null) unless the
    /// fixture says otherwise" - ActualStart is set comfortably before the window so every §3.7 check
    /// passes by default; callers override individual activities for W-08a/b.</summary>
    public static Dictionary<Guid, EotActivityContext> DefaultActivityContexts()
    {
        var start = D(2026, 6, 15);
        return new Dictionary<Guid, EotActivityContext>
        {
            [AId] = new(AId, "A", "Activity A", start, start, null),
            [BId] = new(BId, "B", "Activity B", start, start, null),
            [CId] = new(CId, "C", "Activity C", start, start, null),
            [DId] = new(DId, "D", "Activity D", start, start, null),
        };
    }

    /// <summary>Builds a real, internally-consistent <see cref="CpmRun"/> by actually running
    /// <see cref="CpmEngine.Calculate"/> against <paramref name="activities"/>/<paramref name="relations"/> -
    /// exactly mirroring <c>RecalculateCpmCommandHandler</c>'s own construction, so a fixture's
    /// expected numbers can never silently collude with a transcription error in this test file.</summary>
    public static CpmRun BuildRun(
        DateTimeOffset calculatedAt, IReadOnlyList<(Guid Id, int Duration)> activities, IReadOnlyList<CpmRelationInput> relations)
    {
        var activityInputs = activities.Select(a => new CpmActivityInput(a.Id, a.Duration)).ToList();
        var calc = CpmEngine.Calculate(activityInputs, relations);
        if (calc.IsFailure)
        {
            throw new InvalidOperationException($"Fixture network failed to calculate: {calc.Error}");
        }

        var durationById = activities.ToDictionary(a => a.Id, a => a.Duration);
        var runActivityInputs = calc.Value.Activities
            .Select(r => new CpmRunActivityInput(
                r.ActivityId, durationById[r.ActivityId], r.EarlyStart, r.EarlyFinish, r.LateStart, r.LateFinish,
                r.TotalFloat, r.FreeFloat, r.IsCritical))
            .ToList();
        var runRelationInputs = relations
            .Select(r => new CpmRunRelationInput(r.PredecessorActivityId, r.SuccessorActivityId, r.RelationType, r.LagDays))
            .ToList();

        return CpmRun.Capture(
            TenantId, ProjectId, calculatedAt, dataDate: null, calc.Value.ProjectDurationDays, ActorUserId,
            CpmRunTrigger.Manual, runActivityInputs, runRelationInputs);
    }

    /// <summary>Network N1 verbatim from cpm-method.md / domain-rules.md §10.0: A(5)->B(3)->D(4);
    /// A(5)->C(6)->D(4). $D^{as} = 15$, critical path A-C-D, $TF_B = 3$.</summary>
    public static CpmRun BuildN1Run(DateTimeOffset calculatedAt) =>
        BuildRun(calculatedAt, [(AId, 5), (BId, 3), (CId, 6), (DId, 4)], N1Relations);

    /// <summary>Network N1' from fixture W-11b/W-15: A(5)->B(7)->D(4); A(5)->C(6)->D(4). $D^{as} = 16$,
    /// $TF_B = 0$, $TF_C = 1$ - the critical path has shifted to A-B-D relative to N1.</summary>
    public static CpmRun BuildN1PrimeRun(DateTimeOffset calculatedAt) =>
        BuildRun(calculatedAt, [(AId, 5), (BId, 7), (CId, 6), (DId, 4)], N1Relations);

    // --- Network N2 (fixture W-19 only) - P/Q/R, fresh ids (never mixed with N1's) so no fixture can
    // accidentally cross-wire an N1 activity context into an N2 CpmRun or vice versa. ---
    public static readonly Guid N2PId = Guid.NewGuid();
    public static readonly Guid N2QId = Guid.NewGuid();
    public static readonly Guid N2RId = Guid.NewGuid();

    /// <summary>$P(5) \xrightarrow{SS,0} Q(6)$; $P(5) \xrightarrow{FS,0} R(4)$; $Q(6) \xrightarrow{FS,0} R(4)$
    /// - domain-rules.md §10 fixture W-19's own network, verbatim. $D^{as} = 10$, all three critical
    /// ($TF = 0$ throughout) - independently verified against <see cref="CpmEngine"/>, not hand-copied
    /// from the domain document's own worked backward pass.</summary>
    public static readonly IReadOnlyList<CpmRelationInput> N2Relations =
    [
        new(N2PId, N2QId, RelationType.SS, 0),
        new(N2PId, N2RId, RelationType.FS, 0),
        new(N2QId, N2RId, RelationType.FS, 0),
    ];

    public static CpmRun BuildN2Run(DateTimeOffset calculatedAt) =>
        BuildRun(calculatedAt, [(N2PId, 5), (N2QId, 6), (N2RId, 4)], N2Relations);

    public static Dictionary<Guid, EotActivityContext> N2ActivityContexts()
    {
        var start = D(2026, 6, 15);
        return new Dictionary<Guid, EotActivityContext>
        {
            [N2PId] = new(N2PId, "P", "Piling (N2)", start, start, null),
            [N2QId] = new(N2QId, "Q", "Formwork (N2)", start, start, null),
            [N2RId] = new(N2RId, "R", "Superstructure (N2)", start, start, null),
        };
    }

    // --- Network N3 (fixture W-20 only) - A/X/C/D, fresh ids of their own (deliberately NOT N1's AId/
    // CId/DId, even though the domain document reuses those letters for exposition - "this fixture
    // only" in the document's own words; sharing N1's actual Guids here would risk a fixture silently
    // reading N1 activity/context data into an N3 network by accident). ---
    public static readonly Guid N3AId = Guid.NewGuid();
    public static readonly Guid N3XId = Guid.NewGuid();
    public static readonly Guid N3CId = Guid.NewGuid();
    public static readonly Guid N3DId = Guid.NewGuid();

    /// <summary>$A(5) \xrightarrow{FS,0} C(6)$; $X(9) \xrightarrow{FS,0} C(6)$; $C(6) \xrightarrow{FS,0} D(4)$
    /// - domain-rules.md §10 fixture W-20's own network, verbatim. $D^{as} = 19$, critical path X-C-D;
    /// $TF_A = 4$ - A is a predecessor of C but carries float, which is the whole point of the
    /// fixture (least-float-first must keep C, the successor, not A).</summary>
    public static readonly IReadOnlyList<CpmRelationInput> N3Relations =
    [
        new(N3AId, N3CId, RelationType.FS, 0),
        new(N3XId, N3CId, RelationType.FS, 0),
        new(N3CId, N3DId, RelationType.FS, 0),
    ];

    public static CpmRun BuildN3Run(DateTimeOffset calculatedAt) =>
        BuildRun(calculatedAt, [(N3AId, 5), (N3XId, 9), (N3CId, 6), (N3DId, 4)], N3Relations);

    public static Dictionary<Guid, EotActivityContext> N3ActivityContexts()
    {
        var start = D(2026, 6, 15);
        return new Dictionary<Guid, EotActivityContext>
        {
            [N3AId] = new(N3AId, "A", "Site Clearance (N3)", start, start, null),
            [N3XId] = new(N3XId, "X", "Piling (N3)", start, start, null),
            [N3CId] = new(N3CId, "C", "Foundation (N3)", start, start, null),
            [N3DId] = new(N3DId, "D", "Superstructure (N3)", start, start, null),
        };
    }

    public static EotWeatherEntryInput Entry(
        DateTimeOffset date, WeatherImpact impact, decimal? hoursLost, decimal? rainfallMm, params Guid[] activityIds) =>
        new(Guid.NewGuid(), DateOnly.FromDateTime(date.DateTime), impact, hoursLost, rainfallMm, activityIds);

    public static EotPolicySettings WithPartialDayPolicy(EotPartialDayPolicy policy) =>
        EotPolicySettings.Default with { PartialDayPolicy = policy };

    /// <summary>Convenience builder for the common single-governing-run case (W-01 through W-14,
    /// W-16): every entry's date maps to <paramref name="governingRun"/> directly (a "direct hit",
    /// no degraded-mode fallback engaged) unless the caller supplies its own
    /// <paramref name="governingRunsByDate"/> to exercise §4.4's degraded modes.</summary>
    public static EotEvaluationInput BuildInput(
        IReadOnlyList<EotWeatherEntryInput> entries,
        CpmRun governingRun,
        EotPolicySettings? policy = null,
        IReadOnlyDictionary<Guid, EotActivityContext>? activities = null,
        DateTimeOffset? windowStart = null,
        DateTimeOffset? windowEnd = null,
        DateTimeOffset? evaluatedAt = null,
        IReadOnlyDictionary<DateOnly, CpmRun>? governingRunsByDate = null,
        CpmRun? earliestRunFallback = null) =>
        new(
            ProjectId,
            windowStart ?? WindowStart,
            windowEnd ?? WindowEnd,
            evaluatedAt ?? DefaultEvaluatedAt,
            policy ?? EotPolicySettings.Default,
            Th6DayCalendar(),
            entries,
            activities ?? DefaultActivityContexts(),
            governingRunsByDate ?? entries.Select(e => e.LogDate).Distinct().ToDictionary(dt => dt, _ => governingRun),
            earliestRunFallback ?? governingRun);

    /// <summary>§5.3's cap invariant - "the same calendar day can never yield more than one EOT day" -
    /// asserted on every fixture, not merely trusted (per the domain document's own instruction).</summary>
    public static void AssertCap(EotEvaluationOutcome outcome) =>
        Xunit.Assert.True(
            outcome.EotEligibleDays <= outcome.DistinctCountableDateCount,
            $"E={outcome.EotEligibleDays} must be <= DistinctCountableDateCount={outcome.DistinctCountableDateCount} (§5.3 cap).");
}
