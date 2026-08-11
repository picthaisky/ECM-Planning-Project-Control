using CMPlus.Application.Abstractions;
using CMPlus.Application.Services.Evm;

namespace CMPlus.Application.Features.Evm;

/// <summary>The full result of one EVM computation at a resolved data date - everything both
/// <c>GetEvmQueryHandler</c> and <c>CloseEvmPeriodCommandHandler</c> need, so neither re-implements
/// the settings-lookup -&gt; activity-read -&gt; engine -&gt; calculator pipeline independently
/// (design.md §1.1's own call sequence for `GET .../evm`, reused verbatim for the snapshot command -
/// this is also what lets Sprint 8's Cash Flow module honour its own "no calculation outside the EVM
/// engine" rule a sprint early, docs/10 §7 S8-BE-01).</summary>
/// <param name="ActualCostEntryCount">The number of <c>ActualCostEntry</c> rows that contributed to
/// <see cref="EvmCoreResult.Ac"/> (i.e. with <c>IncurredDate &lt;= DataDate</c>) - carried through
/// from <see cref="CMPlus.Application.Abstractions.ActualCostResult"/> so a future caller can
/// distinguish "no cost recorded yet" from "costs recorded, netting to zero" (actual-cost.md §7.6/
/// AC-5 vs AC-6) without a second query. Deliberately not surfaced on <c>EvmResponseDto</c> in this
/// change - see the backend-developer report for why that is a deferred, not forgotten, decision.
/// </param>
public sealed record EvmComputation(
    ProjectEvmSettings Settings,
    DateTimeOffset DataDate,
    EvmCoreResult Core,
    EacCalculationResult EacResult,
    int ActualCostEntryCount);

/// <summary>
/// S7-BE-03/05 shared orchestration: reader(s) -&gt; <see cref="EvmEngine"/> -&gt;
/// <see cref="EacCalculator"/>. Not a MediatR handler itself - injected into the two handlers that
/// need it, exactly like <c>IApprovalRoutingService</c> is a plain injected service rather than a
/// request.
/// </summary>
public sealed class EvmComputationService(IEvmDataReader dataReader, IActualCostReader actualCostReader)
{
    /// <summary><see langword="null"/> if <paramref name="projectId"/> does not exist (or belongs to
    /// another tenant - ADR-0002). <paramref name="dataDate"/> omitted defaults to
    /// `Project.DataDate` (see <c>GetEvmQuery</c>'s remarks on why this endpoint treats the query
    /// parameter as optional).</summary>
    public async Task<EvmComputation?> ComputeAsync(
        Guid projectId, DateTimeOffset? dataDate, CancellationToken cancellationToken = default)
    {
        var settings = await dataReader.GetProjectSettingsAsync(projectId, cancellationToken);
        if (settings is null)
        {
            return null;
        }

        var effectiveDataDate = dataDate ?? settings.ProjectDataDate;

        // domain-rules.md §5.5(c): BAC(t), not a live scalar read - resolved only once the effective
        // data date is known (it may itself come from settings.ProjectDataDate above), so this is a
        // deliberate second reader call rather than something GetProjectSettingsAsync could supply.
        var bac = await dataReader.GetBacAsOfAsync(projectId, effectiveDataDate, cancellationToken);
        var activityInputs = await dataReader.GetActivityInputsAsync(projectId, effectiveDataDate, cancellationToken);
        var actualCost = await actualCostReader.GetActualCostAsOfAsync(projectId, effectiveDataDate, cancellationToken);

        var ev = EvmEngine.ComputeEarnedValue(activityInputs);
        var pv = EvmEngine.ComputePlannedValue(activityInputs, effectiveDataDate);
        var core = EvmEngine.Compute(bac, pv, ev, actualCost.Amount);
        var eacResult = EacCalculator.ComputeAll(
            core, settings.EacCustomPerformanceFactor, settings.EacManualEtc, settings.EacManualEtcStaleSince is not null);

        return new EvmComputation(settings, effectiveDataDate, core, eacResult, actualCost.EntryCount);
    }
}
