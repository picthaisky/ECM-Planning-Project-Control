namespace CMPlus.Application.Features.Cpm.Commands.RecalculateCpm;

/// <summary>S5-BE-04 DoD summary echoed back to the caller (and what the S5-FE-01 "recalculate
/// CPM" button/critical-path preview consumes). <see cref="CriticalPath"/> is in the graph's own
/// topological order (see <c>CpmCalculationResult.CriticalPath</c>'s remarks on the multi-chain
/// simplification).</summary>
public sealed record RecalculateCpmResultDto(
    int ActivitiesProcessed,
    int CriticalActivityCount,
    int ProjectDurationDays,
    IReadOnlyList<Guid> CriticalPath);
