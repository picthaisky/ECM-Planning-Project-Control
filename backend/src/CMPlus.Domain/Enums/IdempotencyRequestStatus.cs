namespace CMPlus.Domain.Enums;

/// <summary>Lifecycle of an <c>IdempotencyKey</c> reservation (S13-BE-01/S13-DB-01, ADR-0005,
/// closes security review sprint-12.md M-01). A reservation is created <see cref="InProgress"/> and
/// transitions exactly once to <see cref="Completed"/> - never back, mirroring
/// <see cref="ImportJobStatus"/>'s identical "one terminal transition" shape. There is deliberately
/// no <c>Failed</c>/terminal-error state: a request that ends in an unexpected server error
/// (&gt;= 500) is never memoized at all - <c>EfIdempotencyStore.ReleaseAsync</c> deletes the row
/// outright so a future retry can attempt the operation again, rather than caching a transient
/// failure forever (see <c>IdempotencyMiddleware</c>'s remarks).</summary>
public enum IdempotencyRequestStatus
{
    InProgress = 1,
    Completed = 2,
}
