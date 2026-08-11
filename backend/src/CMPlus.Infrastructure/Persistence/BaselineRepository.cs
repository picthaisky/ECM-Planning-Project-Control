using System.Text.Json;
using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>S14-BE-01: <see cref="IBaselineRepository"/> against <see cref="CmPlusDbContext"/>.
/// <see cref="AddAsync"/> mirrors <see cref="CpmScheduleRepository.SaveResultsAsync"/>'s
/// <c>SuppressPerEntityAudit</c> + one-summarizing-row shape for its own append-only-children write,
/// but - unlike that method's Activity write-back half - never needs the raw-SQL/OPENJSON technique
/// at all: a baseline capture is a pure insert of never-before-seen rows (no seek-then-modify), the
/// same shape <c>CpmRuns.Add(run)</c> itself already establishes as acceptable to persist via
/// ordinary EF Core tracking.</summary>
public sealed class BaselineRepository(
    CmPlusDbContext dbContext, ITenantProvider tenantProvider, ICurrentUserContext currentUser, IDateTimeProvider clock)
    : IBaselineRepository
{
    public Task<decimal?> GetProjectBacAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        dbContext.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => (decimal?)p.BAC)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<BaselineActivitySourceRow>> GetCurrentActivitiesAsync(
        Guid projectId, CancellationToken cancellationToken = default)
    {
        // One bounded query, mirrors GanttActivityReader/EvmDataReader's "WBSNodes.Any(...)" join
        // shape rather than an `IN (...)` over up to ~10,000 activity ids - never one query per
        // activity.
        var query = dbContext.Activities
            .AsNoTracking()
            .Where(a => dbContext.WBSNodes.Any(w => w.Id == a.WbsNodeId && w.ProjectId == projectId))
            .Select(a => new BaselineActivitySourceRow(a.Id, a.PlannedStart, a.PlannedFinish, a.DurationDays, a.BudgetCost));

        return await query.ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Baseline baseline, CancellationToken cancellationToken = default)
    {
        dbContext.Baselines.Add(baseline);

        dbContext.SuppressPerEntityAudit = true;
        try
        {
            // One summarizing AuditLog row for the whole capture (CLAUDE.md: "every mutating domain
            // operation writes an audit log entry"), anchored on the Baseline itself rather than
            // Project - mirrors CpmScheduleRepository.SaveResultsAsync's identical reasoning
            // ("which [thing] changed" is the discovery axis a PM/Planning role actually queries
            // by), applied to the aggregate this operation actually creates. Without this escape
            // hatch, AuditSaveChangesInterceptor's default per-entity behaviour would also add one
            // AuditLog row per BaselineActivitySnapshot - 10,000+ extra rows at reference scale for
            // a single capture.
            dbContext.AuditLogs.Add(new AuditLog(
                tenantProvider.TenantId, nameof(Baseline), baseline.Id, AuditAction.Created, currentUser.UserId,
                beforeJson: null,
                afterJson: JsonSerializer.Serialize(new
                {
                    baseline.ProjectId,
                    baseline.Name,
                    ActivityCount = baseline.Snapshots.Count,
                    baseline.Bac,
                }),
                clock.UtcNow));

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            dbContext.SuppressPerEntityAudit = false;
        }
    }

    public Task<Baseline?> FindAsync(Guid baselineId, CancellationToken cancellationToken = default) =>
        dbContext.Baselines.FirstOrDefaultAsync(b => b.Id == baselineId, cancellationToken);

    public Task<Baseline?> FindActiveAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        dbContext.Baselines.FirstOrDefaultAsync(b => b.ProjectId == projectId && b.IsActive, cancellationToken);

    /// <summary>
    /// <b>S14-BE-01 defect fix - see <see cref="IBaselineRepository.TryActivateAsync"/>'s remarks for
    /// the full rationale/evidence.</b>
    ///
    /// <para><b>Why this guards on <c>Database.IsRelational()</c> instead of unconditionally opening a
    /// transaction the way <see cref="CpmScheduleRepository.SaveResultsAsync"/> does - a deliberate,
    /// evidence-driven deviation from that precedent, not an oversight.</b> The obvious first attempt
    /// mirrored that precedent exactly (unconditional <c>BeginTransactionAsync</c>, with the one InMemory
    /// caller - <c>BaselinesControllerTests</c>, via the solution-wide shared
    /// <c>CustomWebApplicationFactory</c> - adding
    /// <c>ConfigureWarnings(w =&gt; w.Ignore(InMemoryEventId.TransactionIgnoredWarning))</c> to that
    /// factory's <c>CmPlusDbContext</c> registration, exactly as
    /// <c>RecalculateCpmCommandHandlerCpmRunCaptureTests</c> already does locally for the identical
    /// exception on <see cref="CpmScheduleRepository"/>). That was built and run - and reproducibly
    /// broke 188 of the Integration suite's 544 tests (spanning dozens of unrelated feature test
    /// classes: Import, WbsNodes, CashFlow, ActualCosts, ...), deterministically, across repeated
    /// clean rebuilds, including with xUnit test-collection parallelization forced off (so not a
    /// parallel-execution race). Removing only that one <c>ConfigureWarnings</c> line restored the
    /// suite to its expected 2 (Baseline-only, pre-fix) failures every time. The exact EF Core internal
    /// mechanism was not fully root-caused (suspected interaction with EF's internal-service-provider
    /// or compiled-model caching, since <c>CustomWebApplicationFactory</c>'s <c>CmPlusDbContext</c>
    /// registration is the ONE shared entry point reused by every WebApi Integration test class in the
    /// solution) - what is certain, reproduced directly, is that adding a warnings-ignore to that one
    /// shared registration is unsafe in this codebase, regardless of the underlying cause. Guarding on
    /// <c>IsRelational()</c> here instead avoids the shared fixture entirely: <see cref="CustomWebApplicationFactory"/>
    /// (test project) needed no change at all for this fix. This is also arguably more correct on its
    /// own terms, not merely a workaround: <see cref="RecalculateCpmCommandHandlerCpmRunCaptureTests"/>'s
    /// own remarks already establish that "InMemory's single <c>SaveChangesAsync</c> call is already
    /// atomic on its own, transaction wrapper or not" - which is exactly the justification for skipping
    /// the wrapper outright on a non-relational provider rather than opening one just to immediately
    /// suppress the warning about it doing nothing. Production always runs against SQL Server
    /// (<c>IsRelational()</c> true), where the transaction is real, exactly as
    /// <c>database-engineer</c>'s SQLite probe (also relational) verified 30/30.</para>
    /// </summary>
    public async Task<bool> TryActivateAsync(Baseline target, Baseline? previousActive, CancellationToken cancellationToken = default)
    {
        var isRelational = dbContext.Database.IsRelational();
        var transaction = isRelational
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            // Deactivate-and-save FIRST, entirely on its own - target.Activate() below is not called
            // until AFTER this SaveChangesAsync returns, so the change tracker has nothing but the
            // deactivate to flush here. This is the load-bearing difference from the single-SaveChanges
            // shape it replaces: there is no batch of two Modified siblings left for EF's internal
            // ordering to get wrong, because the second Modified entry (target) does not exist yet
            // when this statement is generated.
            if (previousActive is not null)
            {
                previousActive.Deactivate();
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            // Only now does target.Activate() happen, mutating the tracker for the second, separate
            // SaveChangesAsync - by the time this INSERT-free UPDATE is issued, the deactivate (if
            // any) is already durable within the transaction, so the filtered unique index never
            // observes two active rows for the same (TenantId, ProjectId), not even momentarily.
            target.Activate();
            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Mirrors ProjectRepository.TrySaveChangesAsync's exact discipline (see that type's
            // remarks): a concurrency-token mismatch is always an expected, reportable failure.
            // No explicit rollback call needed - disposing an uncommitted transaction below rolls it
            // back.
            return false;
        }
        catch (DbUpdateException ex) when (UniqueIndexViolationClassifier.IsUniqueConstraintViolation(ex))
        {
            // The genuine two-*request* race this method's two-SaveChanges split does not (and
            // cannot) prevent on its own: it only removes the single-batch statement-ordering
            // non-determinism (docs/perf/s14-baseline-storage.md §2) for one caller's own two writes -
            // it does nothing to stop a second, independent caller from loading the same
            // `previousActive` before this caller's transaction commits, then activating a *different*
            // target. That second caller's own activate SaveChangesAsync then collides with this
            // transaction's already-committed result against the real
            // `(TenantId, ProjectId) WHERE IsActive = 1` filtered unique index - a plain
            // DbUpdateException (never DbUpdateConcurrencyException: no optimistic-concurrency token
            // is involved anywhere in this sequence, every individual SaveChangesAsync call updates
            // exactly the one row it targeted; EF only raises DbUpdateConcurrencyException from its
            // own affected-row-count check, not from a provider-level constraint rejection).
            // UniqueIndexViolationClassifier deliberately narrows this to exactly that one shape (see
            // its own remarks for why a bare `catch (DbUpdateException)` here would repeat the exact
            // mistake ProjectRepository.TrySaveChangesAsync's doc comment warns against) - anything
            // else still propagates unhandled below, unchanged from before this fix.
            // Proof: CMPlus.Integration.Tests.BaselineOrdering.BaselineActivationOrderingSqliteTests.
            // TryActivateAsync_Reports_A_Clean_Failure_Not_An_Escaped_Exception_When_Two_Concurrent_Requests_Race
            // reproduces this exact two-DbContext race against a real SQLite filtered unique index;
            // reverting this catch clause to DbUpdateConcurrencyException-only makes that test fail
            // with the raw DbUpdateException escaping.
            return false;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }
}
