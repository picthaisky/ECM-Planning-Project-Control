using System.Collections.Concurrent;

namespace CMPlus.Infrastructure.Idempotency;

/// <summary>Ref-counted keyed <see cref="SemaphoreSlim"/> table - see <see cref="IIdempotencyKeyLock"/>
/// for why this exists. Registered as a singleton (<c>DependencyInjection.AddInfrastructure</c>) so
/// the same table is shared across every request in the process, unlike the per-request-scoped
/// <c>CmPlusDbContext</c>/<c>EfIdempotencyStore</c> that use it.</summary>
public sealed class IdempotencyKeyLock : IIdempotencyKeyLock
{
    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int RefCount;
    }

    // Plain `lock` (not the semaphore itself) guards only the tiny, synchronous ref-counted
    // add/remove bookkeeping below - the actual (potentially long) wait happens on the semaphore,
    // outside this lock, so no awaited call ever happens while `_gate` is held.
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<IAsyncDisposable> AcquireAsync(Guid tenantId, string key, CancellationToken cancellationToken)
    {
        var lockKey = $"{tenantId:N}:{key}";

        Entry entry;
        lock (_gate)
        {
            entry = _entries.GetOrAdd(lockKey, static _ => new Entry());
            entry.RefCount++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            // Never acquired the semaphore - undo the refcount bump so this entry is still eligible
            // for cleanup, exactly as if AcquireAsync had never been called.
            ReleaseRef(lockKey, entry, releaseSemaphore: false);
            throw;
        }

        return new Releaser(this, lockKey, entry);
    }

    private void ReleaseRef(string lockKey, Entry entry, bool releaseSemaphore)
    {
        if (releaseSemaphore)
        {
            entry.Semaphore.Release();
        }

        lock (_gate)
        {
            entry.RefCount--;
            if (entry.RefCount == 0)
            {
                _entries.TryRemove(lockKey, out _);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class Releaser(IdempotencyKeyLock owner, string lockKey, Entry entry) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            // Idempotent - a `using` combined with an explicit early dispose must never double-release.
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.ReleaseRef(lockKey, entry, releaseSemaphore: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
