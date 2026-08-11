using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Interceptors;
using CMPlus.Integration.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Manpower;

/// <summary>
/// S12-QA: independently verifies domain-rules.md (manpower-equipment) §4.7's literal words - "Audit
/// invariant for QA (one query, permanent regression guard): AuditLog never contains an Update or
/// Delete row for EntityName = 'ManpowerEquipmentLog', over the whole database, ever" (M-13 step 7) -
/// with the REAL interceptor trio wired together (<c>AuditSaveChangesInterceptor</c> +
/// <c>RowVersionSaveChangesInterceptor</c> + <c>AppendOnlyGuardInterceptor</c>, registration order
/// exactly matching <c>CMPlus.Infrastructure.DependencyInjection</c>), mirroring
/// <c>WeatherLogAuditInvariantTests</c>' identical proof for <see cref="DailyWeatherLog"/>.
/// </summary>
public class ManpowerLogAuditInvariantTests
{
    private sealed class FixedDateTimeProvider(DateTimeOffset now) : CMPlus.Application.Abstractions.IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FixedCurrentUserContext(Guid? userId) : CMPlus.Application.Abstractions.ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;

        public UserRole Role => UserRole.Site;
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
    public async Task A_Blocked_Rewrite_Attempt_Leaves_Zero_Update_Or_Delete_AuditLog_Rows_For_ManpowerEquipmentLog()
    {
        var tenantId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var now = DateTimeOffset.Parse("2026-07-09T18:00:00+07:00");

        Guid logId;
        await using (var seedContext = CreateContext(databaseName, tenantProvider, actorUserId, now))
        {
            var log = ManpowerEquipmentLog.CreateOriginal(
                tenantId, Guid.NewGuid(), DateTimeOffset.Parse("2026-07-09T00:00:00+07:00"), Shift.Day, Guid.NewGuid(),
                Guid.NewGuid(), null, LabourType.OwnDirect, null, 25, 200.00m, 0m, false, 0, 0m, 0m,
                "งานโครงสร้าง", null, actorUserId, now, allowDuplicateOverride: false);
            seedContext.ManpowerEquipmentLogs.Add(log);
            await seedContext.SaveChangesAsync();
            logId = log.Id;
        }

        // Attempt 1: rewrite (Modified) - must throw and leave no Updated audit row.
        await using (var tamperContext = CreateContext(databaseName, tenantProvider, actorUserId, now))
        {
            var log = await tamperContext.ManpowerEquipmentLogs.SingleAsync(m => m.Id == logId);
            tamperContext.Entry(log).Property(nameof(ManpowerEquipmentLog.WorkerCount)).CurrentValue = 999;

            await Assert.ThrowsAsync<InvalidOperationException>(() => tamperContext.SaveChangesAsync());
        }

        // Attempt 2: delete - must also throw and leave no Deleted audit row.
        await using (var deleteContext = CreateContext(databaseName, tenantProvider, actorUserId, now))
        {
            var log = await deleteContext.ManpowerEquipmentLogs.SingleAsync(m => m.Id == logId);
            deleteContext.ManpowerEquipmentLogs.Remove(log);

            await Assert.ThrowsAsync<InvalidOperationException>(() => deleteContext.SaveChangesAsync());
        }

        await using var verifyContext = CreateContext(databaseName, tenantProvider, actorUserId, now);

        var manpowerLogAuditRows = await verifyContext.AuditLogs
            .Where(a => a.EntityName == nameof(ManpowerEquipmentLog))
            .ToListAsync();

        Assert.DoesNotContain(manpowerLogAuditRows, a => a.Action is AuditAction.Updated or AuditAction.Deleted);
        var createdRow = Assert.Single(manpowerLogAuditRows);
        Assert.Equal(AuditAction.Created, createdRow.Action);

        var survivingLog = await verifyContext.ManpowerEquipmentLogs.AsNoTracking().SingleAsync(m => m.Id == logId);
        Assert.Equal(25, survivingLog.WorkerCount);
        Assert.Equal(1, await verifyContext.ManpowerEquipmentLogs.CountAsync());
    }
}
