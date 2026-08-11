using System.Text.Json;
using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>S5-BE-04: <see cref="ICpmScheduleRepository"/> against <see cref="CmPlusDbContext"/>.
/// <see cref="LoadScheduleGraphAsync"/> mirrors <see cref="BatchProgressRepository"/>'s query shape
/// (Sprint 3/4 precedent) for the <c>Activities</c> half; <see cref="SaveResultsAsync"/> deliberately
/// does not follow that precedent for the Activity write-back (see the interface's remarks on why a
/// change-tracked <c>SaveChangesAsync</c> over 10,000 entities measured too slowly to call "bulk").
/// </summary>
public sealed class CpmScheduleRepository(
    CmPlusDbContext dbContext, ITenantProvider tenantProvider, ICurrentUserContext currentUser, IDateTimeProvider clock)
    : ICpmScheduleRepository
{
    public async Task<CpmProjectContext?> GetProjectContextAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        // Single-row projection, same query shape/cost as the bare AnyAsync existence check this
        // replaces (ADR-0019) - DataDate is non-nullable on Project itself, so a null result here
        // means only one thing: no such project in this tenant (ADR-0002).
        var dataDate = await dbContext.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => (DateTimeOffset?)p.DataDate)
            .SingleOrDefaultAsync(cancellationToken);

        return dataDate is null ? null : new CpmProjectContext(dataDate.Value);
    }

    public async Task<CpmScheduleGraph> LoadScheduleGraphAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        // Deliberately tracked (no AsNoTracking) - RecalculateCpmCommandHandler still mutates these
        // via Activity.SetCpmResults for domain-level correctness/testability (see
        // ICpmScheduleRepository's remarks on why persistence itself no longer relies on this).
        // One query, regardless of how many activities the project has.
        var activities = await dbContext.Activities
            .Where(a => dbContext.WBSNodes.Any(w => w.Id == a.WbsNodeId && w.ProjectId == projectId))
            .ToListAsync(cancellationToken);

        // A second, bounded query - never one per activity/relation, and re-derives project
        // membership through the real (indexed) Activities/WBSNodes tables for BOTH the
        // predecessor and successor side, rather than passing a client-materialized collection of
        // this project's activity ids through two ANDed `.Contains()` calls.
        //
        // That `.Contains()` shape was tried first and looked reasonable (EF Core 10 translates
        // HashSet<Guid>.Contains into one parameterized OPENJSON-backed IN clause, avoiding the
        // 2,100-SQL-parameter ceiling) - but using the SAME OPENJSON(@json) parameter in two ANDed
        // `IN (SELECT ... FROM OPENJSON(@json))` clauses is a real, measured SQL Server query-plan
        // pathology: the optimizer did not cache/spool the parsed JSON once, and instead reprocessed
        // it once per outer ActivityRelations row. Reproduced directly in SQL Server, isolated from
        // .NET/EF entirely: 10,000 ids x 15,000 relations took ~82 SECONDS with the double-OPENJSON-IN
        // shape; the exact same result set via this EXISTS-against-real-tables shape took ~20ms (see
        // the Sprint 5 backend-developer report for the full before/after SQL and reasoning). No
        // client-side id collection is parameterized into this query at all any more.
        var relations = activities.Count == 0
            ? []
            : await dbContext.ActivityRelations
                .AsNoTracking()
                .Where(r =>
                    dbContext.Activities.Any(p => p.Id == r.PredecessorActivityId
                        && dbContext.WBSNodes.Any(w => w.Id == p.WbsNodeId && w.ProjectId == projectId))
                    && dbContext.Activities.Any(s => s.Id == r.SuccessorActivityId
                        && dbContext.WBSNodes.Any(w => w.Id == s.WbsNodeId && w.ProjectId == projectId)))
                .ToListAsync(cancellationToken);

        return new CpmScheduleGraph(activities.ToDictionary(a => a.Id), relations);
    }

    public async Task SaveResultsAsync(
        Guid projectId, IReadOnlyList<CpmActivityWriteBack> results, CpmRun run, CancellationToken cancellationToken = default)
    {
        // Real measurement against the S4-DB-02 10,000-activity/15,000-relation dataset: routing
        // this through EF Core's ordinary change-tracked SaveChangesAsync() (10,000 modified
        // Activity entities => 10,000 individual UPDATE statements under the hood, batched a few at
        // a time but still one execution per row) took ~90 seconds end to end, with SQL Server
        // pegged at ~1 CPU core the entire time and the API container essentially idle - a
        // real, measured "not actually bulk" defect, not a hypothetical one. This single
        // OPENJSON-driven UPDATE...FROM...JOIN statement is genuinely set-based - one execution,
        // one plan, regardless of row count - and measured well under a second for the same
        // dataset (see the Sprint 5 backend-developer report for the exact before/after numbers).
        // Unlike LoadScheduleGraphAsync's relations query above, this OPENJSON is only ever
        // referenced ONCE in the statement, so it does not hit that same reprocessing pathology.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (results.Count > 0)
        {
            var payload = JsonSerializer.Serialize(results.Select(r => new
            {
                id = r.ActivityId,
                ic = r.IsCritical,
                tf = r.TotalFloat,
                ff = r.FreeFloat,
            }));

            // TenantId is included explicitly in the JOIN predicate as defense-in-depth (ADR-0002):
            // raw SQL bypasses CmPlusDbContext's global tenant query filter entirely (that filter
            // only applies to LINQ queries), so this statement must not rely solely on "the ids
            // were already tenant-scoped when LoadScheduleGraphAsync selected them" - it re-asserts
            // the same tenant boundary itself.
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $@"UPDATE a
                   SET a.IsCritical = j.IsCritical, a.TotalFloat = j.TotalFloat, a.FreeFloat = j.FreeFloat
                   FROM dbo.Activities a
                   INNER JOIN OPENJSON({payload}) WITH (
                       Id uniqueidentifier '$.id',
                       IsCritical bit '$.ic',
                       TotalFloat int '$.tf',
                       FreeFloat int '$.ff'
                   ) j ON a.Id = j.Id
                   WHERE a.TenantId = {tenantProvider.TenantId};",
                cancellationToken);

            // The Activity instances LoadScheduleGraphAsync returned are still tracked and were
            // mutated in-memory (Activity.SetCpmResults) by the caller before this ran - clear the
            // tracker now so the SaveChangesAsync below does not ALSO try to persist those same rows
            // a second time via individual UPDATE statements, silently doubling the very cost this
            // method exists to avoid. This MUST happen before `run` is added below, or Clear() would
            // detach the run graph too and it would never be saved.
            dbContext.ChangeTracker.Clear();
        }

        // ADR-0019: the append-only CpmRun history, captured in the SAME transaction as the Activity
        // write-back above so a run is never persisted without the Activity state it was computed
        // from (or vice versa) - if either half fails, both roll back together. Ordinary tracked Add
        // (cascades CpmRun.Activities/Relations via the owned-collection navigations) rather than
        // the raw-SQL OPENJSON technique the UPDATE above uses - see ICpmScheduleRepository's remarks
        // on why (chiefly: this keeps run capture exercisable by an EF Core InMemory-backed
        // integration test, which ExecuteSqlInterpolatedAsync cannot be at all).
        dbContext.CpmRuns.Add(run);

        dbContext.SuppressPerEntityAudit = true;
        try
        {
            // One summarizing AuditLog row for the whole recalculation (S5-BE-04 DoD), anchored on
            // the Project - same rationale as BatchProgressRepository.SaveBatchAsync ("which
            // project changed" is the discovery axis a PM/Planning role actually queries by). Also
            // names the captured CpmRunId (ADR-0019) so the audit trail cross-references the run
            // without needing a second row.
            dbContext.AuditLogs.Add(new AuditLog(
                tenantProvider.TenantId, nameof(Project), projectId, AuditAction.Updated, currentUser.UserId,
                beforeJson: null,
                afterJson: JsonSerializer.Serialize(new
                {
                    ProjectId = projectId,
                    ActivitiesRecalculated = results.Count,
                    CpmRunId = run.Id,
                }),
                clock.UtcNow));

            // Persists both the CpmRun graph (Added, tracked above) and the one AuditLog row - the
            // SuppressPerEntityAudit escape hatch means AuditSaveChangesInterceptor does not also
            // add one AuditLog row per CpmRunActivity/CpmRunRelation (which at 10,000/15,000 scale
            // would be 25,000+ extra rows on top of the one intended above).
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            dbContext.SuppressPerEntityAudit = false;
        }
    }
}
