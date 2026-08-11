using CMPlus.Application.Abstractions;
using CMPlus.Application.Services.Wbs;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>S8-BE-02: <see cref="IWbsProgressReader"/> against <see cref="CmPlusDbContext"/>. One
/// query, <c>AsNoTracking</c>, projected to exactly the three scalars the rollup calculator needs -
/// same "join via WBSNodes.Any(...)" shape <see cref="EvmDataReader.GetActivityInputsAsync"/>
/// already uses, since <c>Activity</c> only reaches its project indirectly through
/// <c>WbsNodeId -&gt; WBSNode.ProjectId</c>. Backed by the existing
/// <c>(TenantId, WbsNodeId)</c> index (<c>ActivityConfiguration</c>) - no new index required.
/// </summary>
public sealed class WbsProgressReader(CmPlusDbContext dbContext) : IWbsProgressReader
{
    public async Task<IReadOnlyList<WbsRollupActivityInput>> GetActivityProgressByNodeAsync(
        Guid projectId, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Activities
            .AsNoTracking()
            .Where(a => dbContext.WBSNodes.Any(w => w.Id == a.WbsNodeId && w.ProjectId == projectId))
            .Select(a => new { a.WbsNodeId, a.BudgetCost, a.ProgressPercentage })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new WbsRollupActivityInput(r.WbsNodeId, r.BudgetCost, r.ProgressPercentage))
            .ToList();
    }
}
