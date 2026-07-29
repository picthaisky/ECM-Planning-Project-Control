using CMPlus.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>
/// Infrastructure implementation of <see cref="IActivityProgressReader"/> (S1-BE-04) - the
/// ADR-0009 step-function SQL: the greatest <c>PeriodEndDate</c> &lt;= <c>asOf</c>, ties broken by
/// the greatest <c>RecordedAt</c>, 0 if nothing recorded yet. Relies on
/// <see cref="CmPlusDbContext"/>'s global tenant query filter (ADR-0002) - no explicit TenantId
/// predicate is needed here. <c>AsNoTracking</c> plus the
/// <c>(TenantId, ActivityId, PeriodEndDate DESC)</c> index (docs/db-conventions.md §6) keep this a
/// seek, not a scan.
/// </summary>
public sealed class ActivityProgressReader(CmPlusDbContext dbContext) : IActivityProgressReader
{
    public async Task<decimal> GetProgressAsOfAsync(Guid activityId, DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        var latest = await dbContext.ActivityProgressLogs
            .AsNoTracking()
            .Where(l => l.ActivityId == activityId && l.PeriodEndDate <= asOf)
            .OrderByDescending(l => l.PeriodEndDate)
            .ThenByDescending(l => l.RecordedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return latest?.ProgressPercentage ?? 0m;
    }
}
