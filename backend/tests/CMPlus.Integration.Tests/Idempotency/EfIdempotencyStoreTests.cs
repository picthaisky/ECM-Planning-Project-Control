using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using CMPlus.Infrastructure.Idempotency;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Integration.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CMPlus.Integration.Tests.Idempotency;

/// <summary>
/// S13-BE-01/S13-DB-01: <see cref="EfIdempotencyStore"/> against a real <see cref="CmPlusDbContext"/>
/// (EF Core InMemory, per the Docker outage - see <see cref="TestDbContextFactory"/>'s established
/// rationale). Each test builds its own <see cref="CmPlusDbContext"/> per "request" (mirroring
/// <c>PaymentCertificateConcurrencyTests</c>' identical "two independent contexts against the same
/// named InMemory database" shape) sharing one <see cref="IdempotencyKeyLock"/> instance, exactly as
/// production DI shares one singleton lock across every request.
///
/// <para><b>What this environment cannot prove</b> (see <see cref="EfIdempotencyStore"/>'s own
/// remarks): the real unique <c>(TenantId, Key)</c> index's own enforcement. EF Core InMemory accepts
/// two rows with the same (TenantId, Key) without complaint - confirmed directly by
/// <see cref="InMemory_Provider_Does_Not_Enforce_The_Unique_Index_Documenting_What_This_Suite_Cannot_Prove"/>,
/// which is not a desired behaviour, it is a recorded fact about this environment's limits so nobody
/// mistakes the concurrency tests below for a full substitute.</para>
/// </summary>
public class EfIdempotencyStoreTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-11T09:00:00+07:00");
    private const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static CmPlusDbContext CreateContext(string databaseName, Guid tenantId) =>
        new(new DbContextOptionsBuilder<CmPlusDbContext>().UseInMemoryDatabase(databaseName).Options, new FakeTenantProvider(tenantId));

    private static EfIdempotencyStore CreateStore(string databaseName, Guid tenantId, IIdempotencyKeyLock keyLock, IdempotencyOptions? options = null) =>
        new(CreateContext(databaseName, tenantId), keyLock, Options.Create(options ?? new IdempotencyOptions()));

    [Fact]
    public async Task ReserveAsync_On_A_Fresh_Key_Returns_Reserved()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var store = CreateStore(databaseName, tenantId, new IdempotencyKeyLock());

        var reservation = await store.ReserveAsync(tenantId, "key-1", "POST", "/api/v1/x", HashA, Guid.NewGuid(), Now);

        Assert.Equal(IdempotencyReservationOutcome.Reserved, reservation.Outcome);
        Assert.NotNull(reservation.IdempotencyKeyId);
    }

    [Fact]
    public async Task Reserve_After_Completion_With_The_Same_Hash_Replays_The_Stored_Response_And_Creates_No_Second_Row()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var keyLock = new IdempotencyKeyLock();

        var reservation = await CreateStore(databaseName, tenantId, keyLock)
            .ReserveAsync(tenantId, "key-2", "POST", "/api/v1/x", HashA, Guid.NewGuid(), Now);
        Assert.Equal(IdempotencyReservationOutcome.Reserved, reservation.Outcome);

        await CreateStore(databaseName, tenantId, keyLock)
            .CompleteAsync(reservation.IdempotencyKeyId!.Value, 201, "application/json", "{\"id\":\"abc\"}", responseNotReplayable: false, Now.AddSeconds(1));

        // A brand-new "request" (its own store/context), same key, same hash.
        var replay = await CreateStore(databaseName, tenantId, keyLock)
            .ReserveAsync(tenantId, "key-2", "POST", "/api/v1/x", HashA, Guid.NewGuid(), Now.AddMinutes(1));

        Assert.Equal(IdempotencyReservationOutcome.AlreadyCompleted, replay.Outcome);
        Assert.Equal(201, replay.ResponseStatusCode);
        Assert.Equal("application/json", replay.ResponseContentType);
        Assert.Equal("{\"id\":\"abc\"}", replay.ResponseBody);
        Assert.False(replay.ResponseNotReplayable);

        using var verifyContext = CreateContext(databaseName, tenantId);
        var rows = await verifyContext.IdempotencyKeys.Where(k => k.Key == "key-2").ToListAsync();
        Assert.Single(rows);
    }

    [Fact]
    public async Task Reserve_After_Completion_With_A_Different_Hash_Returns_PayloadMismatch()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var keyLock = new IdempotencyKeyLock();

        var reservation = await CreateStore(databaseName, tenantId, keyLock)
            .ReserveAsync(tenantId, "key-3", "POST", "/api/v1/x", HashA, Guid.NewGuid(), Now);
        await CreateStore(databaseName, tenantId, keyLock)
            .CompleteAsync(reservation.IdempotencyKeyId!.Value, 201, "application/json", "{}", responseNotReplayable: false, Now.AddSeconds(1));

        var mismatched = await CreateStore(databaseName, tenantId, keyLock)
            .ReserveAsync(tenantId, "key-3", "POST", "/api/v1/x", HashB, Guid.NewGuid(), Now.AddMinutes(1));

        Assert.Equal(IdempotencyReservationOutcome.PayloadMismatch, mismatched.Outcome);
    }

    [Fact]
    public async Task Reserve_While_Still_InProgress_Returns_InProgressElsewhere_Regardless_Of_Whether_The_Hash_Matches()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var keyLock = new IdempotencyKeyLock();

        var first = await CreateStore(databaseName, tenantId, keyLock)
            .ReserveAsync(tenantId, "key-4", "POST", "/api/v1/x", HashA, Guid.NewGuid(), Now);
        Assert.Equal(IdempotencyReservationOutcome.Reserved, first.Outcome);

        var sameHash = await CreateStore(databaseName, tenantId, keyLock)
            .ReserveAsync(tenantId, "key-4", "POST", "/api/v1/x", HashA, Guid.NewGuid(), Now);
        var differentHash = await CreateStore(databaseName, tenantId, keyLock)
            .ReserveAsync(tenantId, "key-4", "POST", "/api/v1/x", HashB, Guid.NewGuid(), Now);

        Assert.Equal(IdempotencyReservationOutcome.InProgressElsewhere, sameHash.Outcome);
        Assert.Equal(IdempotencyReservationOutcome.InProgressElsewhere, differentHash.Outcome);
    }

    [Fact]
    public async Task Release_Deletes_The_Reservation_So_A_Later_Reserve_Can_Claim_It_Again()
    {
        // Simulates the wrapped handler failing with an unexpected error (>= 500) - the reservation
        // must not permanently block a future, legitimate retry of the same operation.
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var keyLock = new IdempotencyKeyLock();

        var first = await CreateStore(databaseName, tenantId, keyLock)
            .ReserveAsync(tenantId, "key-5", "POST", "/api/v1/x", HashA, Guid.NewGuid(), Now);
        Assert.Equal(IdempotencyReservationOutcome.Reserved, first.Outcome);

        await CreateStore(databaseName, tenantId, keyLock).ReleaseAsync(first.IdempotencyKeyId!.Value);

        var retry = await CreateStore(databaseName, tenantId, keyLock)
            .ReserveAsync(tenantId, "key-5", "POST", "/api/v1/x", HashA, Guid.NewGuid(), Now.AddSeconds(5));

        Assert.Equal(IdempotencyReservationOutcome.Reserved, retry.Outcome);
        Assert.NotEqual(first.IdempotencyKeyId, retry.IdempotencyKeyId);

        using var verifyContext = CreateContext(databaseName, tenantId);
        var rows = await verifyContext.IdempotencyKeys.Where(k => k.Key == "key-5").ToListAsync();
        Assert.Single(rows);
    }

    [Fact]
    public async Task ReleaseAsync_On_An_Already_Released_Id_Is_A_No_Op()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var keyLock = new IdempotencyKeyLock();

        var reservation = await CreateStore(databaseName, tenantId, keyLock)
            .ReserveAsync(tenantId, "key-6", "POST", "/api/v1/x", HashA, Guid.NewGuid(), Now);
        var store = CreateStore(databaseName, tenantId, keyLock);

        await store.ReleaseAsync(reservation.IdempotencyKeyId!.Value);
        var exception = await Record.ExceptionAsync(() => store.ReleaseAsync(reservation.IdempotencyKeyId!.Value));

        Assert.Null(exception);
    }

    // ------------------------------------------------------------------------------------
    // Mutation evidence: two genuinely concurrent Task.WhenAll reserves for a brand-new key, sharing
    // the one IIdempotencyKeyLock instance production DI would also share as a singleton.
    // IdempotencyKeyLockTests separately proves the lock's mutual exclusion deterministically; this
    // proves the STORE's actual end-to-end use of it produces the right outcome (never two Reserved,
    // never two rows).
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task Two_Concurrent_Reserves_For_A_Brand_New_Key_Produce_Exactly_One_Reserved_And_Exactly_One_Row()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var keyLock = new IdempotencyKeyLock();
        const string key = "concurrent-key";

        var task1 = CreateStore(databaseName, tenantId, keyLock).ReserveAsync(tenantId, key, "POST", "/api/v1/x", HashA, Guid.NewGuid(), Now);
        var task2 = CreateStore(databaseName, tenantId, keyLock).ReserveAsync(tenantId, key, "POST", "/api/v1/x", HashA, Guid.NewGuid(), Now);

        var results = await Task.WhenAll(task1, task2);

        Assert.Single(results, r => r.Outcome == IdempotencyReservationOutcome.Reserved);
        Assert.Single(results, r => r.Outcome == IdempotencyReservationOutcome.InProgressElsewhere);

        using var verifyContext = CreateContext(databaseName, tenantId);
        var rows = await verifyContext.IdempotencyKeys.Where(k => k.Key == key).ToListAsync();
        Assert.Single(rows);
    }

    [Fact]
    public async Task Ten_Concurrent_Reserves_For_A_Brand_New_Key_Produce_Exactly_One_Reserved_And_Exactly_One_Row()
    {
        // A wider fan-out than the pairwise test above, to make it harder for the lock's correctness
        // to be an accident of exactly-two-tasks scheduling.
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var keyLock = new IdempotencyKeyLock();
        const string key = "concurrent-key-wide";

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => CreateStore(databaseName, tenantId, keyLock).ReserveAsync(tenantId, key, "POST", "/api/v1/x", HashA, Guid.NewGuid(), Now))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.Single(results, r => r.Outcome == IdempotencyReservationOutcome.Reserved);
        Assert.Equal(9, results.Count(r => r.Outcome == IdempotencyReservationOutcome.InProgressElsewhere));

        using var verifyContext = CreateContext(databaseName, tenantId);
        var rows = await verifyContext.IdempotencyKeys.Where(k => k.Key == key).ToListAsync();
        Assert.Single(rows);
    }

    [Fact]
    public async Task InMemory_Provider_Does_Not_Enforce_The_Unique_Index_Documenting_What_This_Suite_Cannot_Prove()
    {
        // Recorded, not desired: this is exactly why EfIdempotencyStore/IdempotencyKeyLock's own
        // remarks say the DbUpdateException branch in ReserveAsync is unverified in this environment,
        // and why the in-process lock (proven above and in IdempotencyKeyLockTests) is load-bearing
        // here rather than merely defense-in-depth. On real SQL Server the second SaveChangesAsync
        // below would throw DbUpdateException instead of succeeding.
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();

        using (var context = CreateContext(databaseName, tenantId))
        {
            context.IdempotencyKeys.Add(new IdempotencyKey(tenantId, "dup-key", "POST", "/x", HashA, Guid.NewGuid(), Now));
            await context.SaveChangesAsync();
        }

        using (var context = CreateContext(databaseName, tenantId))
        {
            context.IdempotencyKeys.Add(new IdempotencyKey(tenantId, "dup-key", "POST", "/x", HashA, Guid.NewGuid(), Now));
            await context.SaveChangesAsync(); // Would throw DbUpdateException on real SQL Server; does not here.
        }

        using var verifyContext = CreateContext(databaseName, tenantId);
        var count = await verifyContext.IdempotencyKeys.CountAsync(k => k.Key == "dup-key");
        Assert.Equal(2, count); // Two rows for one (TenantId, Key) - the exact thing the unique index (untested here) exists to forbid.
    }

    // ------------------------------------------------------------------------------------
    // Cross-tenant independence (standing requirement: "cross-tenant keys must not collide or leak").
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task The_Same_Key_String_In_Two_Different_Tenants_Never_Collides()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var keyLock = new IdempotencyKeyLock();
        const string key = "shared-key-string";

        var reservationA = await CreateStore(databaseName, tenantA, keyLock).ReserveAsync(tenantA, key, "POST", "/api/v1/x", HashA, Guid.NewGuid(), Now);
        var reservationB = await CreateStore(databaseName, tenantB, keyLock).ReserveAsync(tenantB, key, "POST", "/api/v1/x", HashA, Guid.NewGuid(), Now);

        Assert.Equal(IdempotencyReservationOutcome.Reserved, reservationA.Outcome);
        Assert.Equal(IdempotencyReservationOutcome.Reserved, reservationB.Outcome); // Not InProgressElsewhere - independent tenants.
        Assert.NotEqual(reservationA.IdempotencyKeyId, reservationB.IdempotencyKeyId);

        await CreateStore(databaseName, tenantA, keyLock)
            .CompleteAsync(reservationA.IdempotencyKeyId!.Value, 201, "application/json", "{\"tenant\":\"A\"}", responseNotReplayable: false, Now.AddSeconds(1));

        // Tenant B's own row is still InProgress - completing A's did not leak into or affect B's.
        var checkB = await CreateStore(databaseName, tenantB, keyLock)
            .ReserveAsync(tenantB, key, "POST", "/api/v1/x", HashA, Guid.NewGuid(), Now.AddSeconds(2));
        Assert.Equal(IdempotencyReservationOutcome.InProgressElsewhere, checkB.Outcome);

        // Tenant A replays correctly, unaffected by B's still-open row under the identical key string.
        var checkA = await CreateStore(databaseName, tenantA, keyLock)
            .ReserveAsync(tenantA, key, "POST", "/api/v1/x", HashA, Guid.NewGuid(), Now.AddSeconds(2));
        Assert.Equal(IdempotencyReservationOutcome.AlreadyCompleted, checkA.Outcome);
        Assert.Equal("{\"tenant\":\"A\"}", checkA.ResponseBody);
    }

    // ------------------------------------------------------------------------------------
    // S13-DB-01: the retention sweep - IdempotencyOptions' documented policy, enforced cross-tenant.
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task PurgeExpiredAsync_Deletes_Expired_Completed_And_Stale_InProgress_Rows_Across_Every_Tenant_And_Keeps_The_Rest()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var options = new IdempotencyOptions { CompletedRetention = TimeSpan.FromDays(90), InProgressRetention = TimeSpan.FromDays(1) };

        using (var seedContext = CreateContext(databaseName, tenantA))
        {
            var oldCompletedA = new IdempotencyKey(tenantA, "A-old-completed", "POST", "/x", HashA, Guid.NewGuid(), Now.AddDays(-100));
            oldCompletedA.Complete(201, "application/json", "{}", responseNotReplayable: false, Now.AddDays(-95));
            seedContext.IdempotencyKeys.Add(oldCompletedA);

            var recentCompletedA = new IdempotencyKey(tenantA, "A-recent-completed", "POST", "/x", HashA, Guid.NewGuid(), Now.AddDays(-2));
            recentCompletedA.Complete(201, "application/json", "{}", responseNotReplayable: false, Now.AddDays(-1));
            seedContext.IdempotencyKeys.Add(recentCompletedA);

            seedContext.IdempotencyKeys.Add(new IdempotencyKey(tenantA, "A-stale-inprogress", "POST", "/x", HashA, Guid.NewGuid(), Now.AddDays(-3)));
            seedContext.IdempotencyKeys.Add(new IdempotencyKey(tenantA, "A-fresh-inprogress", "POST", "/x", HashA, Guid.NewGuid(), Now.AddMinutes(-5)));

            await seedContext.SaveChangesAsync();
        }

        using (var seedContextB = CreateContext(databaseName, tenantB))
        {
            // A second tenant's own old completed row - proves the sweep is genuinely cross-tenant,
            // not accidentally scoped to whichever tenant happens to run PurgeExpiredAsync.
            var oldCompletedB = new IdempotencyKey(tenantB, "B-old-completed", "POST", "/x", HashA, Guid.NewGuid(), Now.AddDays(-200));
            oldCompletedB.Complete(201, "application/json", "{}", responseNotReplayable: false, Now.AddDays(-150));
            seedContextB.IdempotencyKeys.Add(oldCompletedB);
            await seedContextB.SaveChangesAsync();
        }

        var maintenance = CreateStore(databaseName, tenantA, new IdempotencyKeyLock(), options);
        var result = await maintenance.PurgeExpiredAsync(Now);

        Assert.Equal(2, result.CompletedRowsDeleted); // A-old-completed + B-old-completed.
        Assert.Equal(1, result.StaleInProgressRowsDeleted); // A-stale-inprogress.
        Assert.Equal(3, result.TotalRowsDeleted);

        using var verifyContext = CreateContext(databaseName, tenantA);
        var remainingKeys = (await verifyContext.IdempotencyKeys.IgnoreQueryFilters().Select(k => k.Key).ToListAsync())
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["A-fresh-inprogress", "A-recent-completed"], remainingKeys);
    }

    [Fact]
    public async Task PurgeExpiredAsync_Is_A_No_Op_When_Nothing_Is_Expired()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();

        using (var seedContext = CreateContext(databaseName, tenantId))
        {
            seedContext.IdempotencyKeys.Add(new IdempotencyKey(tenantId, "fresh", "POST", "/x", HashA, Guid.NewGuid(), Now.AddMinutes(-1)));
            await seedContext.SaveChangesAsync();
        }

        var result = await CreateStore(databaseName, tenantId, new IdempotencyKeyLock()).PurgeExpiredAsync(Now);

        Assert.Equal(0, result.TotalRowsDeleted);

        using var verifyContext = CreateContext(databaseName, tenantId);
        Assert.Equal(1, await verifyContext.IdempotencyKeys.CountAsync());
    }
}
