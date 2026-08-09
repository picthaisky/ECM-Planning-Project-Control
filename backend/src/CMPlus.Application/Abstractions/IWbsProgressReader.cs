using CMPlus.Application.Services.Wbs;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// S8-BE-02: the one additional read <see cref="Services.Wbs.WbsProgressRollupCalculator"/> needs
/// beyond the WBS tree shape it already gets from <see cref="IWbsTreeReader"/> (reused, not
/// duplicated) - every <c>Activity</c>'s current <c>BudgetCost</c>/<c>ProgressPercentage</c>,
/// grouped by its owning <c>WbsNodeId</c>, project-wide in one query (never one round trip per
/// node - the same N+1 trap <see cref="IWbsTreeReader"/>'s own remarks already forbid). Narrowly
/// scoped to this one method (Interface Segregation, matching this codebase's established
/// <see cref="IActualCostReader"/>-vs-<see cref="IActualCostRepository"/>/<see cref="IEvmDataReader"/>
/// pattern) rather than broadening <see cref="IWbsTreeReader"/> beyond its own S4-BE-01 purpose.
/// </summary>
public interface IWbsProgressReader
{
    /// <summary>
    /// Every <c>Activity</c> in the project's current (not historical/dated) state - deliberately
    /// <em>not</em> the ADR-0009 step-function reconstruction <see cref="IEvmDataReader"/> uses,
    /// because the WBS screen this rollup must match to 2dp has no data-date parameter at all
    /// (<c>GetNodeActivitiesQuery</c>/<c>ActivityForProgressDto.CurrentProgressPercentage</c> always
    /// reflects "now"). Caller must have already confirmed the project exists - always returns an
    /// empty list rather than failing for an unknown project id (same convention as
    /// <see cref="IEvmDataReader.GetActivityInputsAsync"/>).
    /// </summary>
    Task<IReadOnlyList<WbsRollupActivityInput>> GetActivityProgressByNodeAsync(
        Guid projectId, CancellationToken cancellationToken = default);
}
