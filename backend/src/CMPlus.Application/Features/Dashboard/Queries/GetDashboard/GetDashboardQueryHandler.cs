using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Evm;
using CMPlus.Application.Services.Wbs;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Dashboard.Queries.GetDashboard;

/// <summary>
/// S8-BE-02 (US-8.1/US-8.3) orchestration: <see cref="EvmComputationService"/> for every cost/
/// schedule KPI tile, plus <see cref="WbsProgressRollupCalculator"/> for the weight-based progress
/// rollup - two independent calculations composed into one response, neither re-implementing the
/// other.
///
/// <para><b>EAC/ETC/VAC always reflect <c>Project.EacVariantDefault</c></b> (DoD) - this handler
/// reads <see cref="Services.Evm.EacCalculationResult.GetVariant"/> for
/// <see cref="EvmComputation.Settings"/>'s own <c>EacVariantDefault</c> specifically, the same
/// already-computed <see cref="Services.Evm.EacCalculationResult"/>
/// <c>EvmComputationService</c> returns for every variant - never a second EAC computation, and
/// never the request-level override <c>GetEvmQuery</c> supports (there is none here by design, see
/// <see cref="GetDashboardQuery"/>'s remarks).</para>
///
/// <para><b>The WBS progress rollup is intentionally not a function of <c>dataDate</c>.</b>
/// evm-formulas.md's rollup formula weighs each node's <em>current</em> progress
/// (<c>Activity.ProgressPercentage</c>, the ADR-0009 cache) - the same figure
/// <c>GetNodeActivitiesQuery</c>/<c>ActivityForProgressDto.CurrentProgressPercentage</c> already
/// shows on the WBS screen, which itself has no date parameter. Computing the rollup from the
/// ADR-0009 step-function reconstruction at <c>dataDate</c> instead would only agree with the WBS
/// screen when <c>dataDate</c> happens to equal "today" for every activity - the DoD's "ตรงกับหน้า
/// WBS ที่ทศนิยม 2 ตำแหน่ง" requires it to agree always, so this deliberately reads current state via
/// <see cref="IWbsProgressReader"/>, independent of the request's <c>dataDate</c>.</para>
/// </summary>
public sealed class GetDashboardQueryHandler(
    EvmComputationService evmComputationService,
    IWbsTreeReader wbsTreeReader,
    IWbsProgressReader wbsProgressReader)
    : IRequestHandler<GetDashboardQuery, Result<DashboardResponseDto>>
{
    public async Task<Result<DashboardResponseDto>> Handle(GetDashboardQuery request, CancellationToken cancellationToken)
    {
        var computation = await evmComputationService.ComputeAsync(request.ProjectId, request.DataDate, cancellationToken);
        if (computation is null)
        {
            return Result<DashboardResponseDto>.Failure(DashboardErrorCodes.ProjectNotFound);
        }

        var selectedVariantResult = computation.EacResult.GetVariant(computation.Settings.EacVariantDefault);

        var nodeRows = await wbsTreeReader.GetNodesWithActivityCountsAsync(request.ProjectId, cancellationToken);
        var activityRows = await wbsProgressReader.GetActivityProgressByNodeAsync(request.ProjectId, cancellationToken);

        var rollup = WbsProgressRollupCalculator.Compute(
            nodeRows.Select(n => new WbsRollupNodeInput(n.Id, n.ParentWbsNodeId, n.WeightPercentage)).ToList(),
            activityRows);

        var response = DashboardResponseDto.From(request.ProjectId, computation, selectedVariantResult, rollup);
        return Result<DashboardResponseDto>.Success(response);
    }
}
