using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Interceptors;
using CMPlus.Integration.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Manpower;

/// <summary>
/// S12-BE-02, fixture M-13: reproduces the M-01 shape exactly (security review sprint-09.md probe 7,
/// already reproduced for <see cref="DailyWeatherLog"/>/<see cref="CpmRun"/>) for
/// <see cref="ManpowerEquipmentLog"/> - a row can be rewritten/deleted through an ordinary
/// <see cref="CmPlusDbContext"/> with no guard interceptor wired in (the baseline domain-rules.md
/// (manpower-equipment) §4.7's use of <see cref="CMPlus.Domain.Common.IAppendOnly"/> closes), and
/// cannot once <see cref="AppendOnlyGuardInterceptor"/> is wired in.
/// </summary>
public class ManpowerLogAppendOnlyGuardTests
{
    private static CmPlusDbContext CreateContext(string databaseName, FakeTenantProvider tenantProvider, bool withGuard)
    {
        var builder = new DbContextOptionsBuilder<CmPlusDbContext>().UseInMemoryDatabase(databaseName);
        if (withGuard)
        {
            builder.AddInterceptors(new AppendOnlyGuardInterceptor());
        }

        return new CmPlusDbContext(builder.Options, tenantProvider);
    }

    private static async Task<(Guid TenantId, Guid LogId, string DatabaseName)> SeedOneLogAsync(bool withGuard)
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var databaseName = Guid.NewGuid().ToString();

        using var context = CreateContext(databaseName, tenantProvider, withGuard);
        var log = ManpowerEquipmentLog.CreateOriginal(
            tenantId, Guid.NewGuid(), DateTimeOffset.Parse("2026-07-09T00:00:00+07:00"), Shift.Day, Guid.NewGuid(),
            Guid.NewGuid(), null, LabourType.OwnDirect, null, 25, 200.00m, 0m, false, 0, 0m, 0m,
            "งานโครงสร้าง", null, Guid.NewGuid(), DateTimeOffset.Parse("2026-07-09T18:00:00+07:00"),
            allowDuplicateOverride: false);
        context.ManpowerEquipmentLogs.Add(log);
        await context.SaveChangesAsync();

        return (tenantId, log.Id, databaseName);
    }

    [Fact]
    public async Task Without_The_Guard_A_ManpowerLogs_ManHours_Can_Still_Be_Rewritten_And_Deleted_Through_An_Ordinary_DbContext()
    {
        var (tenantId, logId, databaseName) = await SeedOneLogAsync(withGuard: false);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using (var tamperContext = CreateContext(databaseName, tenantProvider, withGuard: false))
        {
            var log = await tamperContext.ManpowerEquipmentLogs.SingleAsync(m => m.Id == logId);
            tamperContext.Entry(log).Property(nameof(ManpowerEquipmentLog.ManHours)).CurrentValue = 999.99m;
            await tamperContext.SaveChangesAsync(); // succeeds today - the bug this fix closes.
        }

        using (var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: false))
        {
            var tampered = await verifyContext.ManpowerEquipmentLogs.AsNoTracking().SingleAsync(m => m.Id == logId);
            Assert.Equal(999.99m, tampered.ManHours);
        }

        using (var deleteContext = CreateContext(databaseName, tenantProvider, withGuard: false))
        {
            var log = await deleteContext.ManpowerEquipmentLogs.SingleAsync(m => m.Id == logId);
            deleteContext.ManpowerEquipmentLogs.Remove(log);
            await deleteContext.SaveChangesAsync(); // succeeds today - the bug this fix closes.
        }

        using (var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: false))
        {
            Assert.Equal(0, await verifyContext.ManpowerEquipmentLogs.CountAsync());
        }
    }

    [Theory]
    [InlineData(nameof(ManpowerEquipmentLog.ManHours))]
    [InlineData(nameof(ManpowerEquipmentLog.WorkerCount))]
    [InlineData(nameof(ManpowerEquipmentLog.WorkCategoryId))]
    public async Task With_The_Guard_A_ManpowerLogs_Field_Cannot_Be_Rewritten_Through_An_Ordinary_DbContext(string propertyName)
    {
        var (tenantId, logId, databaseName) = await SeedOneLogAsync(withGuard: true);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using var tamperContext = CreateContext(databaseName, tenantProvider, withGuard: true);
        var log = await tamperContext.ManpowerEquipmentLogs.SingleAsync(m => m.Id == logId);

        object tamperedValue = propertyName switch
        {
            nameof(ManpowerEquipmentLog.ManHours) => 999.99m,
            nameof(ManpowerEquipmentLog.WorkerCount) => 1,
            nameof(ManpowerEquipmentLog.WorkCategoryId) => Guid.NewGuid(),
            _ => throw new InvalidOperationException($"Unhandled property {propertyName}"),
        };
        tamperContext.Entry(log).Property(propertyName).CurrentValue = tamperedValue;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => tamperContext.SaveChangesAsync());
        Assert.Contains(nameof(ManpowerEquipmentLog), ex.Message);
        Assert.Contains("append-only", ex.Message);
    }

    [Fact]
    public async Task With_The_Guard_Deleting_A_ManpowerLog_Throws_And_The_Row_Survives()
    {
        var (tenantId, logId, databaseName) = await SeedOneLogAsync(withGuard: true);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using (var deleteContext = CreateContext(databaseName, tenantProvider, withGuard: true))
        {
            var log = await deleteContext.ManpowerEquipmentLogs.SingleAsync(m => m.Id == logId);
            deleteContext.ManpowerEquipmentLogs.Remove(log);

            await Assert.ThrowsAsync<InvalidOperationException>(() => deleteContext.SaveChangesAsync());
        }

        using var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: true);
        Assert.Equal(1, await verifyContext.ManpowerEquipmentLogs.CountAsync());
    }

    /// <summary>The other, equally important half: the guard must not block the legitimate path -
    /// a correction referencing the original must succeed exactly like an ordinary first-time append
    /// does (M-13 step 3).</summary>
    [Fact]
    public async Task With_The_Guard_Appending_A_Correction_Referencing_The_Original_Still_Succeeds()
    {
        var (tenantId, originalLogId, databaseName) = await SeedOneLogAsync(withGuard: true);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using (var correctionContext = CreateContext(databaseName, tenantProvider, withGuard: true))
        {
            var original = await correctionContext.ManpowerEquipmentLogs.AsNoTracking().SingleAsync(m => m.Id == originalLogId);

            var correction = ManpowerEquipmentLog.CreateCorrection(
                tenantId, original.ProjectId, originalLogId, "ลืมนับ OT 60 ชม.", original.LogDate, Shift.Day,
                original.WorkCategoryId, original.WbsNodeId, null, LabourType.OwnDirect, null, 75, 660.00m,
                60.00m, false, 0, 0m, 0m, null, null, Guid.NewGuid(), DateTimeOffset.Parse("2026-07-10T09:00:00+07:00"));
            correctionContext.ManpowerEquipmentLogs.Add(correction);

            await correctionContext.SaveChangesAsync(); // must NOT throw - Added is not Modified/Deleted.
        }

        using var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: true);
        Assert.Equal(2, await verifyContext.ManpowerEquipmentLogs.CountAsync());
        var original2 = await verifyContext.ManpowerEquipmentLogs.AsNoTracking().SingleAsync(m => m.Id == originalLogId);
        Assert.Null(original2.CorrectsLogId); // the original itself is untouched by the later correction.
        var correction2 = await verifyContext.ManpowerEquipmentLogs.AsNoTracking().SingleAsync(m => m.Id != originalLogId);
        Assert.Equal(originalLogId, correction2.CorrectsLogId);
        Assert.Equal(660.00m, correction2.ManHours);
    }
}
