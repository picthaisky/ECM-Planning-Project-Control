using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Evm;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using MediatR;

namespace CMPlus.Application.Features.CashFlow.Queries.GetCashFlow;

/// <summary>
/// S8-BE-01 (US-8.2). DoD's single non-negotiable: "ไม่มีการคำนวณซ้ำนอก EVM engine" - no calculation
/// duplicated outside the EVM engine. This handler introduces <b>zero</b> new PV/EV/AC/AC-period
/// summation logic. Every figure in <see cref="CashFlowResponseDto"/> traces to exactly one of three
/// sources, all of them already-engine-produced:
///
/// <list type="number">
/// <item>Copied verbatim from <see cref="EvmComputationService.ComputeAsync"/>'s
/// <c>EvmCoreResult</c> (the same pipeline <c>GetEvmQueryHandler</c> calls) - the cumulative headline
/// (<c>Bac</c>/<c>PvCumulative</c>/<c>EvCumulative</c>/<c>AcCumulative</c>) and the trailing "live"
/// period bucket.</item>
/// <item>Copied verbatim from a frozen <see cref="EvmPeriodSnapshot"/> row via
/// <see cref="IEvmSnapshotRepository"/> (the same repository <c>ListEvmSnapshotsQueryHandler</c>
/// uses for the S-Curve's historical points, S7-FE-04) - every closed period bucket's cumulative
/// figures.</item>
/// <item>A plain subtraction between two values from (1)/(2) - e.g. `snapshot.Ac - previousAc` - to
/// derive a period delta from two cumulative figures, exactly the same shape as the engine's own
/// `Cv = Ev - Ac` inside <c>EvmEngine.Compute</c>. actual-cost.md §7.5 proves this subtraction is
/// exact (not an approximation) for AC: `AC_(a,b] = AC(b) - AC(a)` because both cumulative sums are
/// exact partial sums over the same signed ledger, regardless of sign; the same identity holds for
/// PV/EV by construction (each is itself a sum evaluated at a single instant).</item>
/// </list>
///
/// No path in this handler ever queries <c>ActualCostEntry</c>/<c>ActivityProgressLog</c> directly,
/// and no path re-implements <c>EvmEngine</c>/<c>EacCalculator</c>'s arithmetic - a code reviewer can
/// verify the rule holds by checking this file imports no <c>IActualCostReader</c>/<c>IEvmDataReader</c>
/// and issues no query against a <c>DbContext</c> of its own (the only LINQ here is <c>.Skip(1)</c>
/// over an already-fetched, in-memory <see cref="EvmPeriodSnapshot"/> list - list manipulation, not
/// a second data source).
///
/// <para><b>Receipts vs AC (ADR-0013 §5):</b> kept as two separate top-level objects
/// (<see cref="CashFlowResponseDto.AcCumulative"/>/<see cref="CashFlowResponseDto.Periods"/> vs
/// <see cref="CashFlowResponseDto.Receipts"/>) - never summed into one "cash flow" figure.
/// <see cref="CashFlowResponseDto.NetCashPosition"/> is the one legitimate join
/// (actual-cost.md §5's "receipts - AC = funding position"), computed only when receipts are
/// actually available, which today they never are (no <c>PaymentCertificate</c>/
/// <c>ProjectFinanceLedger</c> exists before Sprint 9) - see <see cref="CashFlowReceiptsDto.NotYetAvailable"/>.</para>
///
/// <para><b>Resolved ambiguity:</b> the query collapses `dataDate`/`to` into one parameter (there is
/// no independent `to`) - see <see cref="GetCashFlowQuery"/>'s remarks. This keeps the "as of" date
/// for the headline tiles and the upper bound of the period-bars window always consistent by
/// construction, at the cost of not supporting "show bars up to X while reporting KPIs as of Y" -
/// no consumer of this sprint needs that, and `GetEvmQuery` itself has no such split either.</para>
/// </summary>
public sealed class GetCashFlowQueryHandler(
    EvmComputationService evmComputationService,
    IEvmSnapshotRepository snapshotRepository)
    : IRequestHandler<GetCashFlowQuery, Result<CashFlowResponseDto>>
{
    public async Task<Result<CashFlowResponseDto>> Handle(GetCashFlowQuery request, CancellationToken cancellationToken)
    {
        var current = await evmComputationService.ComputeAsync(request.ProjectId, request.DataDate, cancellationToken);
        if (current is null)
        {
            return Result<CashFlowResponseDto>.Failure(CashFlowErrorCodes.ProjectNotFound);
        }

        var effectiveDataDate = current.DataDate;

        if (request.From is not null && request.From.Value > effectiveDataDate)
        {
            return Result<CashFlowResponseDto>.Failure(CashFlowErrorCodes.InvalidRange);
        }

        var closedSnapshots = await snapshotRepository.ListAsync(
            request.ProjectId, request.From, effectiveDataDate, cancellationToken);

        var baselineResult = await ResolveBaselineAsync(request, closedSnapshots, cancellationToken);
        if (baselineResult.IsFailure)
        {
            return Result<CashFlowResponseDto>.Failure(baselineResult.Error);
        }

        var (previousPv, previousEv, previousAc, previousBoundary, remainingSnapshots) = baselineResult.Value;

        var periods = new List<CashFlowPeriodPointDto>();
        foreach (var snapshot in remainingSnapshots)
        {
            periods.Add(new CashFlowPeriodPointDto(
                previousBoundary,
                snapshot.DataDate,
                IsClosed: true,
                RoundingRules.ToMoney(snapshot.Pv - previousPv),
                RoundingRules.ToMoney(snapshot.Ev - previousEv),
                RoundingRules.ToMoney(snapshot.Ac - previousAc),
                RoundingRules.ToMoney(snapshot.Pv),
                RoundingRules.ToMoney(snapshot.Ev),
                RoundingRules.ToMoney(snapshot.Ac)));

            previousPv = snapshot.Pv;
            previousEv = snapshot.Ev;
            previousAc = snapshot.Ac;
            previousBoundary = snapshot.DataDate;
        }

        // Trailing "live"/still-open bucket: only when there is unclosed time between the last
        // closed snapshot (or the baseline, or project inception) and the effective data date - if
        // the most recent snapshot already sits exactly at the effective data date, there is nothing
        // left to show as "live".
        var warnings = new List<string>(current.EacResult.Warnings);
        if (previousBoundary is null || previousBoundary.Value < effectiveDataDate)
        {
            periods.Add(new CashFlowPeriodPointDto(
                previousBoundary,
                effectiveDataDate,
                IsClosed: false,
                RoundingRules.ToMoney(current.Core.Pv - previousPv),
                RoundingRules.ToMoney(current.Core.Ev - previousEv),
                RoundingRules.ToMoney(current.Core.Ac - previousAc),
                RoundingRules.ToMoney(current.Core.Pv),
                RoundingRules.ToMoney(current.Core.Ev),
                RoundingRules.ToMoney(current.Core.Ac)));
        }
        else if (RoundingRules.ToMoney(previousPv) != RoundingRules.ToMoney(current.Core.Pv)
            || RoundingRules.ToMoney(previousEv) != RoundingRules.ToMoney(current.Core.Ev)
            || RoundingRules.ToMoney(previousAc) != RoundingRules.ToMoney(current.Core.Ac))
        {
            // The last closed snapshot sits exactly at the effective data date, but the live
            // recomputation at that same date no longer agrees with it - a legitimate restatement
            // (ADR-0013 §6.4), not a bug. Compared at money precision on both sides: `previousXxx`
            // is already-rounded whenever it came from a frozen EvmPeriodSnapshot, but
            // `current.Core` is always full unrounded precision (EvmEngine/EacCalculator never
            // round internally) - comparing raw would false-positive on rounding noise alone, not a
            // real restatement. Surfaced as a warning rather than left as two silently disagreeing
            // numbers between this response's top-level cumulative fields (always live, to stay
            // consistent with GetEvmQuery's own headline) and Periods[]'s last closed bucket
            // (frozen at close time).
            warnings.Add(CashFlowWarningCodes.PeriodRestated);
        }

        // ADR-0013 §5 / S8-FE-02 DoD: receipts (certified payments) and AC (actual spend) are two
        // separate ledgers, never merged - and Payment Certificates/ProjectFinanceLedger do not
        // exist until Sprint 9, so there is no data source to query at all (not even an empty table
        // to confirm zero receipts against). Modelled as an honestly-absent series, never a
        // fabricated 0.00/placeholder value.
        var receipts = CashFlowReceiptsDto.NotYetAvailable();

        var response = new CashFlowResponseDto(
            request.ProjectId,
            effectiveDataDate,
            RoundingRules.ToMoney(current.Core.Bac),
            RoundingRules.ToMoney(current.Core.Pv),
            RoundingRules.ToMoney(current.Core.Ev),
            RoundingRules.ToMoney(current.Core.Ac),
            current.ActualCostEntryCount,
            periods,
            receipts,
            NetCashPosition: null, // Receipts unavailable -> the one legitimate join has no right-hand side yet.
            warnings);

        return Result<CashFlowResponseDto>.Success(response);
    }

    private sealed record BaselineResolution(
        decimal PreviousPv,
        decimal PreviousEv,
        decimal PreviousAc,
        DateTimeOffset? PreviousBoundary,
        IReadOnlyList<EvmPeriodSnapshot> RemainingSnapshots);

    /// <summary>
    /// Resolves the cumulative PV/EV/AC "as of just before the first period bucket this response
    /// shows" - 0.00/project-inception when <see cref="GetCashFlowQuery.From"/> is omitted, otherwise
    /// either an existing closed snapshot exactly at that date (preferred - see the remarks below) or
    /// one extra live <see cref="EvmComputationService.ComputeAsync"/> call at that date.
    /// </summary>
    private async Task<Result<BaselineResolution>> ResolveBaselineAsync(
        GetCashFlowQuery request, IReadOnlyList<EvmPeriodSnapshot> closedSnapshots, CancellationToken cancellationToken)
    {
        if (request.From is null)
        {
            return Result<BaselineResolution>.Success(new BaselineResolution(0m, 0m, 0m, null, closedSnapshots));
        }

        if (closedSnapshots.Count > 0 && closedSnapshots[0].DataDate == request.From.Value)
        {
            // A closed snapshot already exists exactly at the requested lower bound - it *is* the
            // baseline. Never separately live-compute "as of From" here too: if a backdated
            // ActualCostEntry/ActivityProgressLog correction has landed since that period closed
            // (ADR-0013 §6.4/ADR-0009), a fresh live call at the same date could legitimately return
            // different figures than the frozen snapshot, which would produce an artificial non-zero
            // "opening" bucket for a period that is otherwise entirely closed history.
            var baselineSnapshot = closedSnapshots[0];
            return Result<BaselineResolution>.Success(new BaselineResolution(
                baselineSnapshot.Pv, baselineSnapshot.Ev, baselineSnapshot.Ac,
                baselineSnapshot.DataDate, closedSnapshots.Skip(1).ToList()));
        }

        var baseline = await evmComputationService.ComputeAsync(request.ProjectId, request.From, cancellationToken);
        if (baseline is null)
        {
            // Unreachable in practice - the project was already confirmed to exist by the caller's
            // own `current` computation moments earlier, and nothing in this request can delete a
            // project mid-flight - but a wrong-but-plausible-looking cash flow is a worse failure
            // mode than a 404 that "shouldn't happen", so this degrades to an explicit error rather
            // than silently substituting zeros (this codebase's established "never fabricate a
            // plausible number" ethos - ADR-0013, actual-cost.md).
            return Result<BaselineResolution>.Failure(CashFlowErrorCodes.ProjectNotFound);
        }

        return Result<BaselineResolution>.Success(new BaselineResolution(
            baseline.Core.Pv, baseline.Core.Ev, baseline.Core.Ac, request.From, closedSnapshots));
    }
}
