using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>ADR-0019: <see cref="ICpmRunHistoryReader"/> against <see cref="CmPlusDbContext"/>.
/// Read-only and entirely ordinary LINQ (no raw SQL), so - unlike
/// <see cref="CpmScheduleRepository.SaveResultsAsync"/>'s Activity write-back - this is fully
/// exercisable against the EF Core InMemory provider.</summary>
public sealed class CpmRunHistoryReader(CmPlusDbContext dbContext) : ICpmRunHistoryReader
{
    public async Task<CpmRun?> GetGoverningRunAsync(
        Guid projectId, DateTimeOffset date, CancellationToken cancellationToken = default) =>
        await dbContext.CpmRuns
            .AsNoTracking()
            .Include(r => r.Activities)
            .Include(r => r.Relations)
            // S11-DB-01 (database-engineer): confirmed via ToQueryString() against the real SqlServer
            // model that two sibling collection Includes on the same root, with no split-query
            // configured, translate to a SINGLE SELECT with two LEFT JOINs off the same parent key -
            // EF Core logs RelationalEventId.MultipleCollectionIncludeWarning for exactly this shape.
            // That is a genuine cartesian product between Activities and Relations for the one
            // matched CpmRun (count(Activities) x count(Relations) rows over the wire before EF
            // re-assembles the graph client-side) - at the 10,000-activity/15,000-relation reference
            // scale this codebase already uses elsewhere, that is up to 150,000,000 rows for a SINGLE
            // governing-run lookup, and EvaluateEotCommandHandler calls this once per distinct
            // countable stoppage date, so a real evaluation could trigger it many times over. No
            // index design fixes a cartesian join - only the query shape can. AsSplitQuery() is the
            // standard, Microsoft-documented fix (3 separate SELECTs: CpmRuns, then
            // CpmRunActivities WHERE CpmRunId IN (...), then CpmRunRelations WHERE CpmRunId IN (...)),
            // each a plain seek against the indexes below. The usual split-query caveat - the
            // separate round trips can observe concurrent writes inconsistently - does not apply to
            // this aggregate specifically: CpmRun/CpmRunActivity/CpmRunRelation are IAppendOnly and
            // are captured once, together, inside CpmScheduleRepository.SaveResultsAsync's single
            // transaction, and never written to again, so there is no later write these three
            // SELECTs could ever observe half of. The EF Core InMemory provider (this project's
            // Integration.Tests) ignores AsSplitQuery() entirely (not relational), so this changes
            // nothing about what those tests exercise.
            .AsSplitQuery()
            .Where(r => r.ProjectId == projectId && r.CalculatedAt <= date)
            // domain-rules.md §4.1's formula ($\arg\max\{CalculatedAt \le d\}$) does not specify a
            // tie-break for two runs sharing the exact same CalculatedAt tick - astronomically
            // unlikely in practice, but not impossible, so this is a documented assumption rather
            // than an unstated one: Id is a time-ordered UUIDv7 (Entity's own remarks), so ordering
            // by it too deterministically favours the more-recently-inserted run.
            .OrderByDescending(r => r.CalculatedAt)
            .ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<CpmRun?> GetEarliestRunAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await dbContext.CpmRuns
            .AsNoTracking()
            .Include(r => r.Activities)
            .Include(r => r.Relations)
            // Same cartesian-join finding/fix as GetGoverningRunAsync above.
            .AsSplitQuery()
            .Where(r => r.ProjectId == projectId)
            // Same tie-break rationale as GetGoverningRunAsync above.
            .OrderBy(r => r.CalculatedAt)
            .ThenBy(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
