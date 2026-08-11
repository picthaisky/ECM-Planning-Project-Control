namespace CMPlus.Application.Abstractions;

/// <summary>
/// Background retention sweep boundary (S13-DB-01) - deliberately separate from
/// <see cref="IIdempotencyStore"/> even though <c>EfIdempotencyStore</c> implements both: this is the
/// one legitimate <b>cross-tenant</b> operation in the idempotency feature (a system maintenance job
/// has no ambient per-request tenant to scope to), so keeping it off the request-time interface makes
/// it structurally impossible for a command/query handler to reach for it by mistake. See
/// <c>EfIdempotencyStore.PurgeExpiredAsync</c> for the retention policy and the (grep-able,
/// ADR-0002-compliant) <c>IgnoreQueryFilters()</c> this needs.
/// </summary>
public interface IIdempotencyKeyMaintenance
{
    Task<IdempotencyPurgeResult> PurgeExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

public sealed record IdempotencyPurgeResult(int CompletedRowsDeleted, int StaleInProgressRowsDeleted)
{
    public int TotalRowsDeleted => CompletedRowsDeleted + StaleInProgressRowsDeleted;
}
