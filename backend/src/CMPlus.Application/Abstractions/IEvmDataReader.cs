using CMPlus.Application.Services.Evm;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// The project-level scalars <see cref="Features.Evm.EvmComputationService"/> needs before it can
/// call <see cref="CMPlus.Application.Services.Evm.EacCalculator"/> - <c>BAC</c> plus the three
/// ADR-0007 EAC fields already living on <c>Project</c> since Sprint 1. <see cref="ProjectDataDate"/>
/// is the fallback data date when a caller omits `?dataDate=` (design.md §2.1's query is documented
/// as optional in this sprint's endpoint - see <c>GetEvmQuery</c>'s remarks).
/// </summary>
public sealed record ProjectEvmSettings(
    decimal Bac,
    DateTimeOffset ProjectDataDate,
    EacVariant EacVariantDefault,
    decimal? EacCustomPerformanceFactor,
    decimal? EacManualEtc);

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

    /// <summary>Every <c>Activity</c> under <paramref name="projectId"/>'s WBS, each carrying its
    /// <c>BudgetCost</c>/<c>PlannedStart</c>/<c>PlannedFinish</c> and its ADR-0009 step-function
    /// progress percentage as of <paramref name="asOf"/> - exactly what <see cref="EvmEngine"/>'s
    /// <c>ComputeEarnedValue</c>/<c>ComputePlannedValue</c> need, and nothing else. Caller must have
    /// already confirmed the project exists (via <see cref="GetProjectSettingsAsync"/>) - this always
    /// returns an empty list rather than failing for an unknown project id.</summary>
    Task<IReadOnlyList<EvmActivityProgressInput>> GetActivityInputsAsync(
        Guid projectId, DateTimeOffset asOf, CancellationToken cancellationToken = default);
}
