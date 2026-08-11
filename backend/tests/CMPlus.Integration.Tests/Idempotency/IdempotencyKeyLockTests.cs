using CMPlus.Infrastructure.Idempotency;

namespace CMPlus.Integration.Tests.Idempotency;

/// <summary>
/// Deterministic proof of <see cref="IdempotencyKeyLock"/>'s mutual exclusion - controls
/// acquire/release order explicitly rather than relying on <c>Task.WhenAll</c> scheduling luck (the
/// same "control the sequence, don't hope for a real race" discipline
/// <c>PaymentCertificateConcurrencyTests</c> already established for RowVersion, applied here to a
/// mutex instead of an optimistic-concurrency token). <c>EfIdempotencyStoreTests</c> separately
/// exercises this same lock through the real store via genuine concurrent <c>Task.WhenAll</c> calls,
/// for end-to-end (if less strictly deterministic) evidence at the store level.
/// </summary>
public class IdempotencyKeyLockTests
{
    [Fact]
    public async Task A_Second_Acquire_For_The_Same_Tenant_And_Key_Blocks_Until_The_First_Is_Released()
    {
        var sut = new IdempotencyKeyLock();
        var tenantId = Guid.NewGuid();

        var first = await sut.AcquireAsync(tenantId, "same-key", CancellationToken.None);
        var secondTask = sut.AcquireAsync(tenantId, "same-key", CancellationToken.None);

        // Give the second caller every opportunity to (wrongly) complete before asserting it hasn't.
        await Task.WhenAny(secondTask, Task.Delay(TimeSpan.FromMilliseconds(200)));
        Assert.False(secondTask.IsCompleted, "A second Acquire for the same (tenant, key) must not complete while the first is still held.");

        await first.DisposeAsync();

        var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(5));
        await second.DisposeAsync();
    }

    [Fact]
    public async Task Different_Keys_Under_The_Same_Tenant_Never_Block_Each_Other()
    {
        var sut = new IdempotencyKeyLock();
        var tenantId = Guid.NewGuid();

        await using var first = await sut.AcquireAsync(tenantId, "key-a", CancellationToken.None);
        await using var second = await sut.AcquireAsync(tenantId, "key-b", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task The_Same_Key_String_Under_Two_Different_Tenants_Never_Blocks_Each_Other()
    {
        var sut = new IdempotencyKeyLock();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var first = await sut.AcquireAsync(tenantA, "shared-key", CancellationToken.None);
        await using var second = await sut.AcquireAsync(tenantB, "shared-key", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Disposing_The_Same_Acquisition_Twice_Does_Not_Throw_And_Does_Not_Corrupt_Later_Use()
    {
        var sut = new IdempotencyKeyLock();
        var tenantId = Guid.NewGuid();

        var held = await sut.AcquireAsync(tenantId, "key", CancellationToken.None);
        await held.DisposeAsync();
        await held.DisposeAsync(); // Idempotent - must not throw SemaphoreFullException.

        // The entry must still be usable afterwards - proves the ref-counted cleanup was not corrupted
        // by the double release.
        await using var reacquired = await sut.AcquireAsync(tenantId, "key", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Sequential_Acquire_Release_Cycles_On_The_Same_Key_Do_Not_Leak_Or_Deadlock()
    {
        var sut = new IdempotencyKeyLock();
        var tenantId = Guid.NewGuid();

        for (var i = 0; i < 50; i++)
        {
            await using var held = await sut.AcquireAsync(tenantId, "recycled-key", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
