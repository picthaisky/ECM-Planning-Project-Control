using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CMPlus.Infrastructure.Idempotency;

/// <summary>
/// <see cref="IIdempotencyStore"/>/<see cref="IIdempotencyKeyMaintenance"/> against
/// <see cref="CmPlusDbContext"/> (S13-BE-01/S13-DB-01). One class implements both interfaces (they
/// are kept separate at the abstraction level specifically so a request-time caller cannot reach the
/// cross-tenant maintenance method by accident - see <see cref="IIdempotencyKeyMaintenance"/>'s
/// remarks) so the request-time and sweep code share one persistence surface without duplicating it.
///
/// <para><b>Concurrency, and what this environment can and cannot prove.</b> <see cref="ReserveAsync"/>
/// combines two independent layers: an in-process <see cref="IIdempotencyKeyLock"/> around the
/// "check, then insert" critical section (real, testable here), and a catch around the insert's own
/// <see cref="DbUpdateException"/> for the real-database case where a second process/instance wins a
/// genuine unique-index race (docs/db-conventions.md §2 - <c>(TenantId, Key)</c> is unique; see the
/// migration). This codebase's established <c>TrySaveChangesAsync</c> helper
/// (<c>ProjectRepository</c>/<c>PaymentCertificateRepository</c>/etc.) catches only
/// <see cref="DbUpdateConcurrencyException"/> - a RowVersion mismatch on an UPDATE - which is a
/// different exception shape from a unique-constraint violation on an INSERT, so it is deliberately
/// not reused here. The EF Core InMemory provider this environment is limited to (Docker cannot
/// start - no SQL Server) enforces no unique indexes at all, so the <see cref="DbUpdateException"/>
/// branch below is exercised by no test this environment can run; only the in-process lock's
/// serialization is proven by execution. Both are real, complementary controls - see
/// <see cref="IIdempotencyKeyLock"/>'s remarks - but only one of them is verified here.</para>
/// </summary>
public sealed class EfIdempotencyStore(
    CmPlusDbContext dbContext, IIdempotencyKeyLock keyLock, IOptions<IdempotencyOptions> options)
    : IIdempotencyStore, IIdempotencyKeyMaintenance
{
    public async Task<IdempotencyReservation> ReserveAsync(
        Guid tenantId,
        string key,
        string requestMethod,
        string requestPath,
        string requestHash,
        Guid requestedByUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var held = await keyLock.AcquireAsync(tenantId, key, cancellationToken);

        var existing = await dbContext.IdempotencyKeys
            .FirstOrDefaultAsync(k => k.TenantId == tenantId && k.Key == key, cancellationToken);

        if (existing is not null)
        {
            return Evaluate(existing, requestHash);
        }

        var record = new IdempotencyKey(tenantId, key, requestMethod, requestPath, requestHash, requestedByUserId, now);
        dbContext.IdempotencyKeys.Add(record);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return IdempotencyReservation.Reserved(record.Id);
        }
        catch (DbUpdateException)
        {
            // See this type's class remarks: on a real database this is the concurrent loser of the
            // unique-index race. Detach the failed Add and re-check reality rather than trusting the
            // exception's *type* alone to mean "unique violation" - if a fresh query still finds
            // nothing, this was a genuinely different failure and must propagate, not be swallowed.
            dbContext.Entry(record).State = EntityState.Detached;

            var afterConflict = await dbContext.IdempotencyKeys
                .AsNoTracking()
                .FirstOrDefaultAsync(k => k.TenantId == tenantId && k.Key == key, cancellationToken);

            if (afterConflict is null)
            {
                throw;
            }

            return Evaluate(afterConflict, requestHash);
        }
    }

    /// <summary>An <see cref="IdempotencyRequestStatus.InProgress"/> row is always 409, regardless of
    /// whether the concurrent payload happens to match (S13-BE-01 design note) - the hash check only
    /// ever runs against an already-<see cref="IdempotencyRequestStatus.Completed"/> row, which is the
    /// one state whose response is actually knowable/replayable.</summary>
    private static IdempotencyReservation Evaluate(IdempotencyKey record, string requestHash)
    {
        if (record.Status == IdempotencyRequestStatus.InProgress)
        {
            return IdempotencyReservation.InProgressElsewhere;
        }

        if (!string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return IdempotencyReservation.PayloadMismatch;
        }

        return IdempotencyReservation.AlreadyCompleted(
            record.ResponseStatusCode!.Value, record.ResponseContentType, record.ResponseBody, record.ResponseNotReplayable);
    }

    public async Task CompleteAsync(
        Guid idempotencyKeyId,
        int responseStatusCode,
        string? responseContentType,
        string? responseBody,
        bool responseNotReplayable,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var record = await dbContext.IdempotencyKeys.FindAsync([idempotencyKeyId], cancellationToken)
            ?? throw new InvalidOperationException(
                $"IdempotencyKey '{idempotencyKeyId}' was not found for CompleteAsync - ReserveAsync must be called first.");

        record.Complete(responseStatusCode, responseContentType, responseBody, responseNotReplayable, now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseAsync(Guid idempotencyKeyId, CancellationToken cancellationToken = default)
    {
        var record = await dbContext.IdempotencyKeys.FindAsync([idempotencyKeyId], cancellationToken);
        if (record is null)
        {
            // Already gone (e.g. a previous release, or a retention sweep) - ReleaseAsync is
            // idempotent by design, matching every mutation in this codebase that can race a
            // background job.
            return;
        }

        dbContext.IdempotencyKeys.Remove(record);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// S13-DB-01's retention sweep - see <see cref="IdempotencyOptions"/>'s class remarks for the
    /// documented policy this enforces. Uses tracked <c>RemoveRange</c>/<c>SaveChangesAsync</c> rather
    /// than the set-based <c>ExecuteDeleteAsync</c> docs/db-conventions.md §8 recommends for bulk
    /// operations generally: that guidance targets perf-sensitive, high-volume, user-facing paths
    /// (CPM write-back over 10,000+ rows), and EF Core's InMemory provider - the only database this
    /// environment can prove anything against (Docker cannot start) - does not support
    /// <c>ExecuteDeleteAsync</c> at all (confirmed against this same provider limitation
    /// <c>ICpmScheduleRepository</c> already documents for <c>ExecuteSqlInterpolatedAsync</c>). This
    /// job is low-frequency (hourly, <see cref="IdempotencyOptions.CleanupInterval"/>) over a table
    /// the retention policy itself keeps bounded, so the tracked-entity cost is immaterial and the
    /// InMemory-testability is worth more than the set-based query would buy here.
    /// </summary>
    public async Task<IdempotencyPurgeResult> PurgeExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var opts = options.Value;
        var completedCutoff = now - opts.CompletedRetention;
        var inProgressCutoff = now - opts.InProgressRetention;

        // Cross-tenant by design: a system maintenance sweep has no ambient per-request tenant to
        // scope to (HttpContextTenantProvider throws outside an authenticated request), and the whole
        // point of this method is to reclaim expired rows across every tenant, not one. Grep-able,
        // explicit, and exactly the ADR-0002 §2 rule 3 exemption this codebase's own convention doc
        // anticipates ("admin cross-tenant tooling needs explicit filter bypass with audit") - the
        // only other production-code precedent is the Tenant DbSet's own identical exemption
        // (docs/db-conventions.md §2 rule 5).
        var expired = await dbContext.IdempotencyKeys
            .IgnoreQueryFilters()
            .Where(k =>
                (k.Status == IdempotencyRequestStatus.Completed && k.CompletedAt != null && k.CompletedAt < completedCutoff) ||
                (k.Status == IdempotencyRequestStatus.InProgress && k.ReservedAt < inProgressCutoff))
            .ToListAsync(cancellationToken);

        if (expired.Count == 0)
        {
            return new IdempotencyPurgeResult(0, 0);
        }

        var completedCount = expired.Count(k => k.Status == IdempotencyRequestStatus.Completed);
        var staleInProgressCount = expired.Count - completedCount;

        dbContext.IdempotencyKeys.RemoveRange(expired);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new IdempotencyPurgeResult(completedCount, staleInProgressCount);
    }
}
