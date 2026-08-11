using CMPlus.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>
/// Real <see cref="IActualCostReader"/> implementation - actual-cost.md §7.1/§9 (ADR-0013):
/// <c>AC(t) = SUM(Amount) WHERE IncurredDate &lt;= asOf</c>, for <c>ActualCostEntry</c> rows
/// belonging to this project. Replaces the Sprint 7 literal-<c>0</c> placeholder now that
/// <see cref="CMPlus.Domain.Entities.ActualCostEntry"/> exists.
///
/// <para>Tenant scoping comes from <see cref="CmPlusDbContext"/>'s global query filter
/// (ADR-0002) - no explicit <c>TenantId</c> predicate is needed here. Project scoping is this
/// query's own explicit <c>ProjectId</c> predicate (unlike <c>Activity</c>/<c>ActivityProgressLog</c>,
/// which only reach their project indirectly via <c>WBSNode</c>, <c>ActualCostEntry</c> carries
/// <c>ProjectId</c> directly - actual-cost.md §9).</para>
///
/// <para>Projects only <c>Amount</c> (never a full-row <c>SELECT *</c>) and aggregates client-side
/// rather than translating a SQL <c>SUM</c>/<c>COUNT</c>, which keeps this portable and identical
/// across the EF Core InMemory provider (used by this sprint's integration tests, per the Docker
/// outage - see the backend-developer report) and real SQL Server, with no GroupBy-translation
/// risk either way. This remains a single covering-index seek at real SQL Server volume - the
/// projected <c>Amount</c> column is already <c>INCLUDE</c>d on the primary
/// <c>(TenantId, ProjectId, IncurredDate)</c> index (actual-cost.md §9) - and row volume is tiny
/// by this codebase's standards regardless (~300/month, "two orders of magnitude below
/// ActivityProgressLog" per actual-cost.md §6.7), so materializing the filtered `Amount` column
/// into memory before summing is not a performance concern the way it would be for a
/// high-cardinality table.</para>
/// </summary>
public sealed class ActualCostReader(CmPlusDbContext dbContext) : IActualCostReader
{
    public async Task<ActualCostResult> GetActualCostAsOfAsync(
        Guid projectId, DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        var amounts = await dbContext.ActualCostEntries
            .AsNoTracking()
            .Where(e => e.ProjectId == projectId && e.IncurredDate <= asOf)
            .Select(e => e.Amount)
            .ToListAsync(cancellationToken);

        // No entries at all (actual-cost.md §7.6/AC-5) -> amount 0.00 (never null), count 0. Entries
        // that net to exactly 0.00 (AC-6) still report their real, non-zero count - the reason
        // ActualCostResult carries a count at all (see that type's remarks).
        return new ActualCostResult(amounts.Sum(), amounts.Count);
    }
}
