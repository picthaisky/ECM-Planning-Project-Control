using CMPlus.Application.Features.Eot;
using CMPlus.Application.Services.Cpm;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Services.Eot;

/// <summary>
/// S11-BE-02: the contemporaneous, windows-based Time Impact Analysis engine (domain-rules.md
/// weather-eot §5). Pure - no I/O, no <c>DbContext</c>, no clock read of its own (the caller supplies
/// <see cref="EotEvaluationInput.EvaluatedAt"/>) - exactly like <c>CpmEngine</c>, and for the same
/// reason: fully unit-testable against the domain-rules.md §10 fixtures with nothing but plain CLR
/// collections, and safely reusable from both the command handler and this project's own tests
/// without a database.
///
/// <para><b>Reuses <c>CpmEngine.Calculate</c> for every network computation - never a second,
/// bespoke float ledger.</b> §5.1 is explicit that this is mandatory ("Do not write a bespoke float
/// ledger... CpmEngine... already handles shared float, criticality shift, parallel paths and all
/// four relation types correctly"), and it is also what makes the DoD's "critical → EOT, non-critical
/// → float only" rule an <i>emergent property</i> of the network computation rather than a branch
/// anywhere in this file - there is no <c>if (isCritical)</c> here at all; float exhaustion, the
/// criticality swap (fixture W-03) and the double-count cap (§5.3, fixture W-07) all fall out of two
/// plain <see cref="CpmEngine.Calculate"/> calls per governing run.</para>
///
/// <para><b>One TIA per governing run, never per arbitrary date slice</b> (§5.1) - countable days are
/// partitioned by which <see cref="CpmRun"/> governs their date (domain-rules.md §4.1's contemporaneous
/// ruling) before any network is built, and $E$ is the sum of each run's own
/// $\max(0, D^{imp}_r - D^{as}_r)$, never a single network's stoppages sliced into windows and summed
/// separately (§5.1's own worked counter-example shows that understates).</para>
///
/// <para><b>§5.2a's serial-chain collapse runs per governing run, before the impacted network is
/// built</b> (domain-expert ruling 2026-08-10, fixing the §5.3 cap breach `qa-engineer` reproduced):
/// on any one date, at most one activity per CPM path may be charged (<see cref="SerialChainCollapse"/>);
/// an activity whose day was absorbed into another still gets a driver row (<c>StoppageDays = 0</c>,
/// <c>SerialChainAbsorbedDays &gt; 0</c>) - it is never silently dropped, and §3.7 itself gains no new
/// gate (a storm genuinely can stop an activity and its own predecessor on the same day; the fix is in
/// the summation, not in countability).</para>
/// </summary>
public static class EotEvaluator
{
    public static Result<EotEvaluationOutcome> Evaluate(EotEvaluationInput input)
    {
        var windowStart = DateOnly.FromDateTime(input.WindowStart.DateTime);
        var windowEnd = DateOnly.FromDateTime(input.WindowEnd.DateTime);
        var activityCodeById = input.Activities.ToDictionary(kv => kv.Key, kv => kv.Value.ActivityCode);

        var sources = new List<EotSourceOutcome>();
        var unattributedCount = 0;
        var countableEntries = new List<(EotWeatherEntryInput Entry, decimal DayWeight, bool HoursLostClampedToFullDay)>();

        // --- §3.1-§3.6 (entry-level gates). §3's InEvalRange gate and "the entry must be in force"
        // are both applied before a candidate ever reaches this loop (window pre-filter below; the
        // caller passes only L^eff) - see EotExclusionReason's remarks on why those two never produce
        // a Source row here. ---
        foreach (var entry in input.EffectiveEntries)
        {
            if (entry.LogDate < windowStart || entry.LogDate > windowEnd)
            {
                continue;
            }

            var gate = EotCountabilityGate.Evaluate(entry, input.Policy, input.Calendar);
            if (!gate.IsCountable)
            {
                if (gate.ExclusionReason == EotExclusionReason.NoAffectedActivity)
                {
                    unattributedCount++;
                }

                sources.Add(new EotSourceOutcome(entry.Id, 0m, gate.ExclusionReason, gate.HoursLostClampedToFullDay));
                continue;
            }

            countableEntries.Add((entry, gate.DayWeight, gate.HoursLostClampedToFullDay));
        }

        // --- §3.7 (per named activity) + accumulate per (governing run, date, activity) day-weights.
        // Date-level granularity (rather than flattening straight into a per-activity total, as before
        // §5.2a existed) is what lets the serial-chain collapse below decide, per date, which
        // activities on a common path may keep their charge. ---
        var perRunDateActivityWeights = new Dictionary<Guid, Dictionary<DateOnly, Dictionary<Guid, decimal>>>();
        var runsUsed = new Dictionary<Guid, CpmRun>();
        var distinctCountableDates = new HashSet<DateOnly>();
        var governingRunByDate = new Dictionary<DateOnly, CpmRun>();

        foreach (var (entry, dayWeight, hoursLostClamped) in countableEntries)
        {
            if (!governingRunByDate.TryGetValue(entry.LogDate, out var governingRun))
            {
                governingRun = input.GoverningRunsByDate.TryGetValue(entry.LogDate, out var directHit)
                    ? directHit
                    : input.EarliestRunFallback;
                governingRunByDate[entry.LogDate] = governingRun;
            }

            EotExclusionReason? firstActivityFailureReason = null;
            var chargedAny = false;

            foreach (var activityId in entry.AffectedActivityIds)
            {
                // Defensive only (never fired by any domain-rules.md fixture): §3.2 attribution is
                // already enforced when a weather log entry is written
                // (RecordWeatherLogCommandHandler.FindExistingActivityIdsAsync), so an unresolvable
                // id here should not occur in practice.
                if (!input.Activities.TryGetValue(activityId, out var activityContext))
                {
                    continue;
                }

                var windowReason = EotCountabilityGate.CheckInWindow(activityContext, entry.LogDate);
                if (windowReason is not null)
                {
                    firstActivityFailureReason ??= windowReason;
                    continue;
                }

                chargedAny = true;
                runsUsed[governingRun.Id] = governingRun;

                if (!perRunDateActivityWeights.TryGetValue(governingRun.Id, out var byDate))
                {
                    byDate = [];
                    perRunDateActivityWeights[governingRun.Id] = byDate;
                }

                if (!byDate.TryGetValue(entry.LogDate, out var byActivity))
                {
                    byActivity = [];
                    byDate[entry.LogDate] = byActivity;
                }

                // Sum, in the rare case more than one entry names the same activity on the same date
                // (e.g. an AM and a PM record) - §5.2a's antichain then sees exactly one combined
                // weight for that (date, activity) pair, matching how §3's own gate output is already
                // additive per entry.
                byActivity[activityId] = byActivity.GetValueOrDefault(activityId) + dayWeight;
            }

            if (chargedAny)
            {
                distinctCountableDates.Add(entry.LogDate);
                sources.Add(new EotSourceOutcome(entry.Id, dayWeight, null, hoursLostClamped));
            }
            else
            {
                // Every named activity failed §3.7 (or none resolved at all, the defensive case
                // above) - the day is excluded for this entry. NoAffectedActivity is only reached
                // here in that never-fired defensive case; §3.2's real "empty list" case was already
                // handled in the entry-level loop above.
                sources.Add(new EotSourceOutcome(entry.Id, 0m, firstActivityFailureReason ?? EotExclusionReason.NoAffectedActivity, hoursLostClamped));
            }
        }

        // --- §4.4 Confidence/CriticalityBasis - based only on dates that actually charged at least
        // one activity, never on dates that were merely candidates (a date where every named activity
        // turned out to be outside its own window never needed a governing run at all). ---
        var anyDirectHit = false;
        var anyFallback = false;
        foreach (var date in distinctCountableDates)
        {
            if (input.GoverningRunsByDate.ContainsKey(date))
            {
                anyDirectHit = true;
            }
            else
            {
                anyFallback = true;
            }
        }

        var (criticalityBasis, confidence) = (anyDirectHit, anyFallback) switch
        {
            // Also the zero-countable-day vacuous case (W-16a/b): a valid, Substantiated evaluation
            // answering "0" is still evidence, not the absence of one.
            (_, false) => (EotCriticalityBasis.Contemporaneous, EotConfidence.Substantiated),
            (true, true) => (EotCriticalityBasis.Mixed, EotConfidence.Provisional),
            (false, true) => (EotCriticalityBasis.Retrospective, EotConfidence.Provisional),
        };

        // --- §5.1: one contemporaneous windows TIA per governing run, in chronological order. ---
        var runOutcomes = new List<EotRunOutcome>();
        var driverOutcomes = new List<EotDriverOutcome>();
        var totalEotEligibleDays = 0;
        var totalAsScheduled = 0;
        var totalImpacted = 0;
        var countableStoppageDayCount = 0;
        var serialChainAbsorbedDayCount = 0;

        foreach (var run in runsUsed.Values.OrderBy(r => r.CalculatedAt).ThenBy(r => r.Id))
        {
            var runActivityIds = run.Activities.Select(a => a.ActivityId).ToHashSet();
            var relationInputs = run.Relations
                .Select(r => new CpmRelationInput(r.PredecessorActivityId, r.SuccessorActivityId, r.RelationType, r.LagDays))
                .ToList();
            var asScheduledActivityInputs = run.Activities
                .Select(a => new CpmActivityInput(a.ActivityId, a.DurationDays))
                .ToList();
            var runActivityById = run.Activities.ToDictionary(a => a.ActivityId);

            var asScheduled = CpmEngine.Calculate(asScheduledActivityInputs, relationInputs);
            if (asScheduled.IsFailure)
            {
                return Result<EotEvaluationOutcome>.Failure(asScheduled.Error);
            }

            // §5.1: "assert they agree; a mismatch means the stored run is corrupt and the
            // evaluation must fail rather than proceed" - re-running CpmEngine against the run's own
            // captured network must reproduce the CpmRun's own stored ProjectDurationDays exactly.
            if (asScheduled.Value.ProjectDurationDays != run.ProjectDurationDays)
            {
                return Result<EotEvaluationOutcome>.Failure(EotErrorCodes.CpmRunDataCorrupt);
            }

            // §5.2a's tie-break keys: TotalFloatAtRun (the stored run's own figure - what every
            // driver row itself reports), the run's own topological order (CpmCalculationResult's
            // Activities list is already in Kahn's-algorithm order - "ties go upstream-first"), and
            // ActivityCode ordinal (the final, deterministic tie-break).
            var topologicalIndex = asScheduled.Value.Activities
                .Select((a, i) => (a.ActivityId, Index: i))
                .ToDictionary(x => x.ActivityId, x => x.Index);
            var totalFloatAtRun = runActivityById.ToDictionary(kv => kv.Key, kv => kv.Value.TotalFloat);

            // --- §5.2a: per date, collapse the charged set to a maximal antichain of ≺_r before any
            // duration is added to the impacted network - this is what makes §5.3's cap a theorem
            // rather than an accident of which fixtures happened to only ever charge parallel
            // activities (see this run's own worked derivation in the domain document, W-17/W-19/W-20). ---
            Dictionary<DateOnly, Dictionary<Guid, decimal>> weightsByDateForRun = perRunDateActivityWeights.TryGetValue(run.Id, out var byDateForRun)
                ? byDateForRun
                : [];
            var collapse = SerialChainCollapse.Collapse(
                weightsByDateForRun, runActivityIds, relationInputs, totalFloatAtRun, topologicalIndex, activityCodeById);

            // Only activities this run actually knows about can be impacted by it - an activity
            // charged under a run that predates its own creation cannot be represented in that run's
            // network (defensive; not exercised by any domain-rules.md fixture, all of which keep a
            // stable activity set across runs). SerialChainCollapse already filtered both dictionaries
            // to runActivityIds, so no further membership check is needed here.
            var deltaByActivity = new Dictionary<Guid, (int Delta, decimal? Unclaimed)>();
            foreach (var (activityId, weights) in collapse.KeptWeightsByActivity)
            {
                var (delta, unclaimed) = ComputeDayCount(weights, input.Policy);
                deltaByActivity[activityId] = (delta, unclaimed);
                countableStoppageDayCount += delta;
            }

            var absorbedDaysByActivity = new Dictionary<Guid, int>();
            foreach (var (activityId, weights) in collapse.AbsorbedWeightsByActivity)
            {
                var (absorbedDelta, _) = ComputeDayCount(weights, input.Policy);
                absorbedDaysByActivity[activityId] = absorbedDelta;
                serialChainAbsorbedDayCount += absorbedDelta;
            }

            var impactedActivityInputs = run.Activities
                .Select(a => new CpmActivityInput(
                    a.ActivityId, a.DurationDays + (deltaByActivity.TryGetValue(a.ActivityId, out var d) ? d.Delta : 0)))
                .ToList();

            var impacted = CpmEngine.Calculate(impactedActivityInputs, relationInputs);
            if (impacted.IsFailure)
            {
                return Result<EotEvaluationOutcome>.Failure(impacted.Error);
            }

            var dAs = asScheduled.Value.ProjectDurationDays;
            var dImp = impacted.Value.ProjectDurationDays;
            // Defensive only - see §5.1's own remark: adding non-negative durations to a max-plus
            // network can never shorten it, so this max(0, .) never actually clamps anything for any
            // run that reached this point with >=1 charged day.
            var runDelta = Math.Max(0, dImp - dAs);

            totalEotEligibleDays += runDelta;
            totalAsScheduled += dAs;
            totalImpacted += dImp;

            var datesForRun = distinctCountableDates.Where(d => governingRunByDate[d].Id == run.Id).ToList();
            var windowFrom = datesForRun.Count > 0 ? datesForRun.Min() : windowStart;
            var windowTo = datesForRun.Count > 0 ? datesForRun.Max() : windowStart;

            runOutcomes.Add(new EotRunOutcome(run.Id, windowFrom, windowTo, dAs, dImp, runDelta));

            var impactedByActivityId = impacted.Value.Activities.ToDictionary(a => a.ActivityId);

            // Driver rows: the UNION of activities charged (kept) and activities absorbed - §5.2a's
            // "nothing is hidden" rule means an absorbed activity still gets its own row
            // (StoppageDays = 0, SerialChainAbsorbedDays > 0), never silently omitted (fixture W-17's
            // C, W-20's A).
            var driverActivityIds = deltaByActivity.Keys.Union(absorbedDaysByActivity.Keys);

            foreach (var activityId in driverActivityIds)
            {
                if (!input.Activities.TryGetValue(activityId, out var activityContext))
                {
                    continue; // defensive - identical guard to the one above.
                }

                var hasKeptDays = deltaByActivity.TryGetValue(activityId, out var keptEntry);
                var delta = hasKeptDays ? keptEntry.Delta : 0;
                var unclaimed = hasKeptDays ? keptEntry.Unclaimed : null;
                var serialChainAbsorbedDays = absorbedDaysByActivity.GetValueOrDefault(activityId);
                string? absorbedIntoActivityCodes = null;
                if (serialChainAbsorbedDays > 0 && collapse.AbsorbedIntoActivityIds.TryGetValue(activityId, out var absorbers))
                {
                    absorbedIntoActivityCodes = string.Join(
                        ", ", absorbers.Select(id => activityCodeById.GetValueOrDefault(id, id.ToString())));
                }

                var runActivity = runActivityById[activityId];
                var tfAtRun = runActivity.TotalFloat;
                var wasCriticalAtRun = runActivity.IsCritical;
                var isOnImpactedCriticalPath = impactedByActivityId[activityId].IsCritical;
                // §5.4's single-activity closed form - exact only when this is the sole affected
                // activity; the network figure (dImp - dAs, above) always governs. Uses the CHARGED
                // (post-collapse) delta, so a fully-absorbed activity reads Indicative = 0 (§5.2a).
                var indicative = Math.Max(0, delta - tfAtRun);
                var remainingFloatAfter = Math.Max(0, tfAtRun - delta);

                int marginal;
                if (delta == 0)
                {
                    // Necessarily 0 for a fully-absorbed activity (delta == 0 here): removing a
                    // charge that was never applied to the impacted network changes nothing (§5.4).
                    marginal = 0;
                }
                else
                {
                    // §5.4: "re-run with only j's stoppages removed" - every OTHER charged
                    // activity keeps its own delta; only this one reverts to its as-scheduled duration.
                    var withoutJInputs = run.Activities
                        .Select(a => new CpmActivityInput(
                            a.ActivityId,
                            a.ActivityId == activityId
                                ? a.DurationDays
                                : a.DurationDays + (deltaByActivity.TryGetValue(a.ActivityId, out var otherDelta) ? otherDelta.Delta : 0)))
                        .ToList();

                    var withoutJ = CpmEngine.Calculate(withoutJInputs, relationInputs);
                    if (withoutJ.IsFailure)
                    {
                        return Result<EotEvaluationOutcome>.Failure(withoutJ.Error);
                    }

                    marginal = dImp - withoutJ.Value.ProjectDurationDays;
                }

                driverOutcomes.Add(new EotDriverOutcome(
                    run.Id, activityId, activityContext.ActivityCode, activityContext.Name,
                    delta, tfAtRun, wasCriticalAtRun, isOnImpactedCriticalPath, indicative, marginal, remainingFloatAfter, unclaimed,
                    serialChainAbsorbedDays, absorbedIntoActivityCodes));
            }
        }

        // --- §3.6: notice, advisory only, zero arithmetic effect on EotEligibleDays. ---
        DateOnly? latestNoticeDate = null;
        bool? noticeWindowExpired = null;
        if (input.Policy.NoticePeriodDays is { } noticeDays && distinctCountableDates.Count > 0)
        {
            var maxCountableDate = distinctCountableDates.Max();
            latestNoticeDate = maxCountableDate.AddDays(noticeDays);
            noticeWindowExpired = DateOnly.FromDateTime(input.EvaluatedAt.DateTime) > latestNoticeDate;
        }

        return Result<EotEvaluationOutcome>.Success(new EotEvaluationOutcome(
            criticalityBasis,
            confidence,
            totalAsScheduled,
            totalImpacted,
            totalEotEligibleDays,
            countableStoppageDayCount,
            distinctCountableDates.Count,
            unattributedCount,
            latestNoticeDate,
            noticeWindowExpired,
            runOutcomes,
            sources,
            driverOutcomes,
            serialChainAbsorbedDayCount));
    }

    /// <summary>§3.4's per-activity, per-governing-run floor: under
    /// <see cref="EotPartialDayPolicy.FractionalAccrual"/>, sum the (already §3.4-clamped, per date)
    /// weights and floor by <c>FullDayHours</c>, reporting the discarded remainder; under the other two
    /// policies each weight is already a whole day (1.00), so the sum is already an integer day count
    /// and there is no remainder to report. Shared between <see cref="SerialChainCollapse"/>'s kept
    /// (feeds $\Delta D_j$) and absorbed (feeds <c>SerialChainAbsorbedDays</c>) weight lists - the same
    /// "per date, before summation" floor rule applies to both (§5.2a/§3.4).</summary>
    private static (int Days, decimal? Unclaimed) ComputeDayCount(List<decimal> weights, EotPolicySettings policy)
    {
        if (weights.Count == 0)
        {
            return (0, null);
        }

        if (policy.PartialDayPolicy == EotPartialDayPolicy.FractionalAccrual)
        {
            var totalHours = weights.Sum();
            var delta = (int)Math.Floor(totalHours / policy.FullDayHours);
            var unclaimed = totalHours - (delta * policy.FullDayHours);
            return (delta, unclaimed);
        }

        return ((int)weights.Sum(), null);
    }
}
