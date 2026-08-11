using CMPlus.Application.Abstractions;
using CMPlus.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>
/// Real <see cref="IProjectFinanceLedgerReader"/> implementation (S9-BE-04) - mirrors
/// <see cref="ActualCostReader"/>'s exact shape: projects only <c>Amount</c> (never a full-row
/// <c>SELECT *</c>) and aggregates client-side after <c>ToListAsync</c> rather than translating a SQL
/// <c>SUM</c>, which keeps this portable and identical across the EF Core InMemory provider (used by
/// this sprint's integration tests, per the Docker outage - see the Sprint 9 backend-developer
/// report) and real SQL Server, with no GroupBy-translation risk either way. Tenant scoping comes
/// from <see cref="CmPlusDbContext"/>'s global query filter (ADR-0002) - no explicit
/// <c>TenantId</c> predicate is needed here.
/// </summary>
public sealed class ProjectFinanceLedgerReader(CmPlusDbContext dbContext) : IProjectFinanceLedgerReader
{
    public async Task<decimal> GetRetentionHeldAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var amounts = await dbContext.ProjectFinanceLedgers
            .AsNoTracking()
            .Where(e => e.ProjectId == projectId && e.Category == FinanceLedgerCategory.Retention)
            .Select(e => e.Amount)
            .ToListAsync(cancellationToken);

        return amounts.Sum();
    }

    public async Task<decimal> GetAdvanceRecoveredAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var amounts = await dbContext.ProjectFinanceLedgers
            .AsNoTracking()
            .Where(e => e.ProjectId == projectId
                        && e.Category == FinanceLedgerCategory.Advance
                        && (e.EntryType == FinanceLedgerEntryType.Recovery || e.EntryType == FinanceLedgerEntryType.Adjustment))
            .Select(e => e.Amount)
            .ToListAsync(cancellationToken);

        return amounts.Sum();
    }
}
