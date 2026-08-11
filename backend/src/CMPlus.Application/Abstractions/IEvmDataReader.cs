using CMPlus.Application.Services.Evm;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// The project-level scalars <see cref="Features.Evm.EvmComputationService"/> needs before it can
/// call <see cref="CMPlus.Application.Services.Evm.EacCalculator"/> - the three ADR-0007 EAC fields
/// already living on <c>Project</c> since Sprint 1, plus <see cref="EacManualEtcStaleSince"/>
/// (domain-rules.md §5.7). <see cref="ProjectDataDate"/> is the fallback data date when a caller
/// omits `?dataDate=` (design.md §2.1's query is documented as optional in this sprint's endpoint -
/// see <c>GetEvmQuery</c>'s remarks).
///
/// <para><b>Deliberately carries no <c>Bac</c> field (S10-QA gap fix).</b> domain-rules.md §5.5(c):
/// $BAC(t) = BAC^{orig} + \sum_{v:\,Approved,\,ApprovedAt \le t} A_v$ - BAC is a function of the data
/// date being computed, not a scalar that can be read before that date is even resolved (a caller's
/// `?dataDate=` may be omitted, in which case <see cref="ProjectDataDate"/> from THIS record supplies
/// the fallback - a circular dependency if this record tried to carry an already-resolved BAC too).
/// See <see cref="IEvmDataReader.GetBacAsOfAsync"/>, called separately once the effective data date is
/// known.</para>
/// </summary>
public sealed record ProjectEvmSettings(
    DateTimeOffset ProjectDataDate,
    EacVariant EacVariantDefault,
    decimal? EacCustomPerformanceFactor,
    decimal? EacManualEtc,
    DateTimeOffset? EacManualEtcStaleSince = null);

/// <summary>
/// S7-BE-01/03: the project-wide read <see cref="EvmEngine"/> needs to compute PV/EV at any data
/// date - reuses the exact ADR-0009 step-function rule Sprint 1's <see cref="IActivityProgressReader"/>
/// already implements (S1-BE-04), just batched project-wide in one query instead of one round trip
/// per activity, so a 10,000-activity project (S7-DB-02's own perf scenario) is a single index-seek-
/// backed query rather than an N+1 loop. This is a deliberate, narrow addition alongside
/// <see cref="IActivityProgressReader"/> - not a replacement for it (that interface's own single-
/// activity shape is still what a per-activity progress display would call) - and it does not
/// re-derive the step-function reconstruction rule itself; the Infrastructure implementation issues
/// the identical "latest `PeriodEndDate` &lt;= asOf, ties broken by `RecordedAt`" query shape,
/// project-wide via a correlated subquery instead of a per-id parameter.
/// </summary>
public interface IEvmDataReader
{
    /// <summary><see langword="null"/> if <paramref name="projectId"/> does not exist (or belongs to
    /// another tenant, indistinguishable under the global query filter - ADR-0002).</summary>
    Task<ProjectEvmSettings?> GetProjectSettingsAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// domain-rules.md §5.5(c): $BAC(t) = BAC^{orig} + \sum_{v:\,Approved,\,ApprovedAt \le t} A_v$ -
    /// reconstructs BAC as of <paramref name="asOf"/> from <c>Project.OriginalBac</c> (frozen at
    /// project creation, ADR-0015-shaped) plus every <c>VariationOrder</c> approved at or before that
    /// instant, rather than reading the live, current <c>Project.BAC</c> unconditionally. This is what
    /// makes a historical read at a data date before a VO's own <c>ApprovedAt</c> keep reporting the
    /// pre-VO figure even after that VO is later approved - the exact guarantee ADR-0009/domain-rules.md
    /// §5.5(a) already gives closed <c>EvmPeriodSnapshot</c> rows, extended here to cover a live/ad-hoc
    /// historical query too (which snapshot immutability alone does not protect).
    /// <see langword="0m"/> if <paramref name="projectId"/> does not exist - caller must have already
    /// confirmed existence via <see cref="GetProjectSettingsAsync"/>, mirroring
    /// <see cref="GetActivityInputsAsync"/>'s own "benign default, never a second not-found signal"
    /// contract.
    /// </summary>
    Task<decimal> GetBacAsOfAsync(Guid projectId, DateTimeOffset asOf, CancellationToken cancellationToken = default);

    /// <summary>Every <c>Activity</c> under <paramref name="projectId"/>'s WBS, each carrying its
    /// <c>BudgetCost</c>/<c>PlannedStart</c>/<c>PlannedFinish</c> and its ADR-0009 step-function
    /// progress percentage as of <paramref name="asOf"/> - exactly what <see cref="EvmEngine"/>'s
    /// <c>ComputeEarnedValue</c>/<c>ComputePlannedValue</c> need, and nothing else. Caller must have
    /// already confirmed the project exists (via <see cref="GetProjectSettingsAsync"/>) - this always
    /// returns an empty list rather than failing for an unknown project id.</summary>
    Task<IReadOnlyList<EvmActivityProgressInput>> GetActivityInputsAsync(
        Guid projectId, DateTimeOffset asOf, CancellationToken cancellationToken = default);
}
