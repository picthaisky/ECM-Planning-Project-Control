namespace CMPlus.Application.Abstractions;

/// <summary>Just enough of a <see cref="Domain.Entities.Baseline"/> to answer "which one, and what
/// was its BAC/name/capture date" - never its (potentially 10,000-row)
/// <see cref="Domain.Entities.Baseline.Snapshots"/>, which <see cref="IBaselineComparisonReader.GetActivityComparisonAsync"/>
/// reads separately as a single bulk projection.</summary>
public sealed record BaselineSummary(Guid Id, string Name, DateTimeOffset CapturedAt, decimal Bac);

/// <summary>
/// One activity's current-vs-baseline component values (S14-BE-02) - deliberately the raw scalars
/// on both sides, not a pre-computed variance. <c>CompareBaselineQueryHandler</c> computes every
/// day/money delta in plain C# after materialization (the same "plain subtraction between two
/// already-fetched values" shape <c>GetCashFlowQueryHandler</c> already establishes), never inside
/// the LINQ projection itself - <c>EF.Functions.DateDiffDay</c> would be the natural in-SQL
/// alternative, but it has no translation on the EF Core InMemory provider this environment is
/// limited to, which would make the query itself untestable here rather than merely unproven at
/// scale.
///
/// <para><see cref="CurrentPlannedStart"/> and its siblings are <see langword="null"/> when no live
/// <see cref="Domain.Entities.Activity"/> matches this snapshot's <c>ActivityId</c> any more (there
/// is no delete path for <see cref="Domain.Entities.Activity"/> anywhere in this codebase today, so
/// this is a defensive allowance for a future one, not a reachable case yet) - a left join anchored
/// on the baseline snapshot, never an inner join, so a removed activity still appears (with only its
/// frozen baseline side populated) rather than silently vanishing from the comparison.</para>
/// </summary>
public sealed record BaselineActivityComparisonRow(
    Guid ActivityId,
    DateTimeOffset BaselinePlannedStart,
    DateTimeOffset BaselinePlannedFinish,
    int BaselineDurationDays,
    decimal BaselineBudgetCost,
    string? CurrentActivityCode,
    string? CurrentName,
    DateTimeOffset? CurrentPlannedStart,
    DateTimeOffset? CurrentPlannedFinish,
    int? CurrentDurationDays,
    decimal? CurrentBudgetCost,
    bool? CurrentIsCritical);

/// <summary>
/// Read side of S14-BE-02's <c>CompareBaselineQuery</c>. Three bounded queries total, none of which
/// grow with activity count except <see cref="GetActivityComparisonAsync"/> itself (a single
/// projected join, never a per-activity round trip) - the same "query count independent of row
/// count" discipline <c>IWbsTreeReader</c>/<c>ICpmScheduleRepository</c> already establish for the
/// other endpoints with a stated 10,000-activity performance target.
/// </summary>
public interface IBaselineComparisonReader
{
    /// <summary><see langword="null"/> if <paramref name="projectId"/> does not exist (or belongs to
    /// another tenant - ADR-0002); otherwise the project's current, live <c>BAC</c> - the
    /// "current" side of the comparison's BAC-variance tile.</summary>
    Task<decimal?> GetCurrentBacAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>The project's currently-active baseline's summary, or <see langword="null"/> if none
    /// is active (a legitimate state - a brand-new project, or one where every captured baseline has
    /// since been deliberately deactivated - never conflated with "project not found").</summary>
    Task<BaselineSummary?> FindActiveBaselineAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>One specific baseline's summary, scoped to <paramref name="projectId"/> - a
    /// <paramref name="baselineId"/> that exists but belongs to a different project (or a different
    /// tenant) is indistinguishable from "does not exist" (ADR-0002, and the same cross-document
    /// fixture precedent <c>ManpowerLogErrorCodes.WbsNodeNotFound</c>'s remarks establish: bare
    /// 404, never a 422 that would confirm the id exists somewhere).</summary>
    Task<BaselineSummary?> FindBaselineAsync(Guid projectId, Guid baselineId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every <see cref="Domain.Entities.BaselineActivitySnapshot"/> under
    /// <paramref name="baselineId"/>, left-joined to its current <see cref="Domain.Entities.Activity"/>
    /// counterpart (by <c>ActivityId</c>) in one query. <paramref name="projectId"/> is accepted for
    /// the same explicit-scoping convention every other project-scoped reader in this codebase
    /// follows (<c>IGanttActivityReader.GetActivitiesAsync</c>, etc.), even though the join is
    /// already transitively project-scoped once the caller has confirmed <paramref name="baselineId"/>
    /// belongs to <paramref name="projectId"/> via <see cref="FindBaselineAsync"/>/
    /// <see cref="FindActiveBaselineAsync"/> first - every <see cref="Domain.Entities.BaselineActivitySnapshot"/>
    /// under a given baseline was, by construction, captured from that same baseline's own project.
    /// </summary>
    Task<IReadOnlyList<BaselineActivityComparisonRow>> GetActivityComparisonAsync(
        Guid projectId, Guid baselineId, CancellationToken cancellationToken = default);
}
