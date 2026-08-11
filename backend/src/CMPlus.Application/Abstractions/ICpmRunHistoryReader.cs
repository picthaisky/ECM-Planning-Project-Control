using CMPlus.Domain.Entities;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// Read side of the append-only <see cref="CpmRun"/> history (ADR-0019, domain-rules.md weather-eot
/// §4.1/§4.3) - the building block the not-yet-built EotEvaluator (S11-BE-02) needs to answer "was
/// this activity critical on the stoppage date". Write side is
/// <see cref="ICpmScheduleRepository.SaveResultsAsync"/>, which captures a new run every time
/// <c>RecalculateCpmCommand</c> succeeds. This reader only selects and returns a run's stored
/// network faithfully; interpreting it (the §4.4 degraded-mode Confidence/CriticalityBasis rules,
/// the §5 windows Time Impact Analysis) is entirely S11-BE-02's job.
/// </summary>
public interface ICpmRunHistoryReader
{
    /// <summary>
    /// The governing run for a stoppage on <paramref name="date"/>:
    /// $r(d) = \arg\max\{r.\mathrm{CalculatedAt} \le d\}$ (domain-rules.md §4.1) - the most recent
    /// recalculation at or before <paramref name="date"/>, tenant- and project-scoped (ADR-0002).
    /// Returns the run together with its <see cref="CpmRun.Activities"/>/<see cref="CpmRun.Relations"/>
    /// (untracked) so a caller can re-run <c>CpmEngine</c> against the network exactly as it stood,
    /// per domain-rules.md §4.3's "not optional" ruling on <see cref="CpmRunRelation"/>.
    /// <see langword="null"/> when no run exists at or before <paramref name="date"/> for this
    /// project - domain-rules.md §4.4's "no run at or before this day" degraded mode, which is the
    /// evaluator's call to interpret (fall back to the earliest run / refuse with
    /// <c>NoCpmRunAvailable</c>), not this reader's.
    /// </summary>
    Task<CpmRun?> GetGoverningRunAsync(Guid projectId, DateTimeOffset date, CancellationToken cancellationToken = default);

    /// <summary>
    /// S11-BE-02: the earliest <see cref="CpmRun"/> ever captured for a project (by
    /// <see cref="CpmRun.CalculatedAt"/> ascending), or <see langword="null"/> when the project has
    /// never had one at all - domain-rules.md §4.4's degraded-mode fallback ("no run at or before
    /// this countable day ⟹ use the earliest run for all/the orphans") and, when this itself returns
    /// <see langword="null"/>, the basis for the evaluator's 422 <c>NoCpmRunAvailable</c> refusal.
    /// Tenant- and project-scoped exactly like <see cref="GetGoverningRunAsync"/>.
    /// </summary>
    Task<CpmRun?> GetEarliestRunAsync(Guid projectId, CancellationToken cancellationToken = default);
}
