using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Interceptors;
using CMPlus.Integration.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Eot;

/// <summary>
/// S11-QA-01: independently verifies domain-rules.md weather-eot §8.3's literal words - "Invariant
/// for QA: the AuditLog table never contains an Update or Delete row for EntityName =
/// 'DailyWeatherLog', over the whole database, ever" (W-10 step 12) - as its own explicit, whole-table
/// assertion with the REAL interceptor trio wired together (<c>AuditSaveChangesInterceptor</c> +
/// <c>RowVersionSaveChangesInterceptor</c> + <c>AppendOnlyGuardInterceptor</c>, registration order
/// exactly matching <c>CMPlus.Infrastructure.DependencyInjection</c>/every production
/// <c>CmPlusDbContext</c>), not merely inferred from "the guard throws" plus "audit exists on Create"
/// as two separate facts proven in two separate files
/// (<c>AppendOnlyGuardInterceptorTests</c>/<c>AuditSaveChangesInterceptorTests</c>).
///
/// <para>Registration order matters here specifically: <c>AuditSaveChangesInterceptor</c> runs BEFORE
/// <c>AppendOnlyGuardInterceptor</c> in the pipeline, so if Audit ever staged an "Updated" AuditLog
/// row for the doomed change before the guard threw, an environment with no real transactional
/// rollback around a thrown <c>SavingChanges</c> exception could theoretically still persist that
/// stray row. This test proves that does not happen (the guard throws inside the SAME
/// <c>SavingChanges</c> pre-save phase, before the call ever reaches the provider's actual persist
/// step, so nothing staged in that call - audit row included - survives).</para>
/// </summary>
public class WeatherLogAuditInvariantTests
{
    private sealed class FixedDateTimeProvider(DateTimeOffset now) : CMPlus.Application.Abstractions.IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FixedCurrentUserContext(Guid? userId) : CMPlus.Application.Abstractions.ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;

        public UserRole Role => UserRole.PM;
    }

    private static CmPlusDbContext CreateContext(string databaseName, FakeTenantProvider tenantProvider, Guid actorUserId, DateTimeOffset now) =>
        new(
            new DbContextOptionsBuilder<CmPlusDbContext>()
                .UseInMemoryDatabase(databaseName)
                .AddInterceptors(
                    new AuditSaveChangesInterceptor(tenantProvider, new FixedCurrentUserContext(actorUserId), new FixedDateTimeProvider(now)),
                    new RowVersionSaveChangesInterceptor(),
                    new AppendOnlyGuardInterceptor())
                .Options,
            tenantProvider);

    [Fact]
    public async Task A_Blocked_Rewrite_Attempt_Leaves_Zero_Update_Or_Delete_AuditLog_Rows_For_DailyWeatherLog()
    {
        var tenantId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var now = DateTimeOffset.Parse("2026-07-11T18:00:00+07:00");

        Guid logId;
        await using (var seedContext = CreateContext(databaseName, tenantProvider, actorUserId, now))
        {
            var log = DailyWeatherLog.CreateOriginal(
                tenantId, Guid.NewGuid(), DateTimeOffset.Parse("2026-07-11T00:00:00+07:00"), WeatherCondition.HeavyRain,
                "ฝนตกหนัก", 42.5m, WeatherImpact.FullStoppage, "หยุดเทคอนกรีตโซน B ครึ่งวัน", 8.00m, actorUserId, now, []);
            seedContext.DailyWeatherLogs.Add(log);
            await seedContext.SaveChangesAsync();
            logId = log.Id;
        }

        // Attempt 1: rewrite (Modified) - must throw and leave no Updated audit row.
        await using (var tamperContext = CreateContext(databaseName, tenantProvider, actorUserId, now))
        {
            var log = await tamperContext.DailyWeatherLogs.SingleAsync(w => w.Id == logId);
            tamperContext.Entry(log).Property(nameof(DailyWeatherLog.ConditionNote)).CurrentValue = "TAMPERED";

            await Assert.ThrowsAsync<InvalidOperationException>(() => tamperContext.SaveChangesAsync());
        }

        // Attempt 2: delete - must also throw and leave no Deleted audit row.
        await using (var deleteContext = CreateContext(databaseName, tenantProvider, actorUserId, now))
        {
            var log = await deleteContext.DailyWeatherLogs.SingleAsync(w => w.Id == logId);
            deleteContext.DailyWeatherLogs.Remove(log);

            await Assert.ThrowsAsync<InvalidOperationException>(() => deleteContext.SaveChangesAsync());
        }

        await using var verifyContext = CreateContext(databaseName, tenantProvider, actorUserId, now);

        // The literal §8.3 invariant: zero Update/Delete AuditLog rows for DailyWeatherLog, over the
        // WHOLE table, after both blocked attempts - not merely "the guard threw" taken on faith.
        var weatherLogAuditRows = await verifyContext.AuditLogs
            .Where(a => a.EntityName == nameof(DailyWeatherLog))
            .ToListAsync();

        Assert.DoesNotContain(weatherLogAuditRows, a => a.Action is AuditAction.Updated or AuditAction.Deleted);
        // The only row that may exist is the original Created one from the seed step.
        var createdRow = Assert.Single(weatherLogAuditRows);
        Assert.Equal(AuditAction.Created, createdRow.Action);

        // And the row itself is provably untouched underneath the failed attempts.
        var survivingLog = await verifyContext.DailyWeatherLogs.AsNoTracking().SingleAsync(w => w.Id == logId);
        Assert.Equal("หยุดเทคอนกรีตโซน B ครึ่งวัน", survivingLog.ImpactNote);
        Assert.Equal(1, await verifyContext.DailyWeatherLogs.CountAsync());
    }
}
