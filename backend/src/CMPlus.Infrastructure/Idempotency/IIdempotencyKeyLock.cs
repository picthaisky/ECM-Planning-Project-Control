namespace CMPlus.Infrastructure.Idempotency;

/// <summary>
/// In-process mutual exclusion keyed by <c>(TenantId, Key)</c>, scoped to
/// <see cref="EfIdempotencyStore"/>'s "does a row already exist, and if not, insert one" critical
/// section - never held across the wrapped handler's own execution (see that type's remarks for why
/// that would be both unnecessary and harmful to throughput).
///
/// <para><b>Why this exists at all, given a real unique index already exists (S13-DB-01
/// <c>(TenantId, Key)</c>).</b> On a real database the unique index alone is sufficient - two
/// concurrent inserts race at the storage engine, exactly one wins, the loser gets a constraint-
/// violation exception. This environment's EF Core InMemory provider does not enforce unique indexes
/// at all (Docker cannot start here - no SQL Server to prove the index actually fires), so without
/// this lock, two concurrent same-key requests would both observe "no existing row" and both insert,
/// silently producing two rows for one key - i.e. exactly the duplicate this feature exists to
/// prevent, undetectable by any test this environment can run. The lock is a genuine, testable
/// single-process guarantee; the unique index remains the cross-instance/real-database guarantee
/// (defense-in-depth, not a substitute for one another) - see <c>EfIdempotencyStore.ReserveAsync</c>'s
/// remarks for how both are wired together.</para>
/// </summary>
public interface IIdempotencyKeyLock
{
    /// <summary>Waits for exclusive access to <paramref name="tenantId"/>/<paramref name="key"/>,
    /// then returns a token that releases it on disposal. Callers must keep the held section as short
    /// as possible (one query plus, on the fast path, one insert).</summary>
    Task<IAsyncDisposable> AcquireAsync(Guid tenantId, string key, CancellationToken cancellationToken);
}
