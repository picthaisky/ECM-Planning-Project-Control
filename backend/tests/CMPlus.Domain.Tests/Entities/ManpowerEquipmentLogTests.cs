using System.Reflection;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Tests.Entities;

/// <summary>
/// S12-BE-02 (US-12.2): <see cref="ManpowerEquipmentLog"/> is immutable claim evidence - verified
/// structurally here (no public mutating method, no public property setter, the same discipline
/// <c>DailyWeatherLogTests</c> already established for its sibling), plus construction/validation
/// (§4.1's table) and correction-chain-shape coverage per
/// <c>docs/specs/manpower-equipment/domain-rules.md</c> §4.7. Persistence-layer enforcement (the
/// actual "cannot be rewritten through an ordinary DbContext" guarantee, fixture M-13) is proven
/// separately in <c>CMPlus.Integration.Tests.Manpower</c>.
/// </summary>
public class ManpowerEquipmentLogTests
{
    private static ManpowerEquipmentLog CreateOriginal(
        Guid? projectId = null,
        Guid? recordedByUserId = null,
        Guid? workCategoryId = null,
        int workerCount = 25,
        decimal manHours = 200.00m,
        decimal overtimeHours = 0m,
        int equipmentCount = 0,
        decimal equipmentOperatingHours = 0m,
        decimal equipmentStandbyHours = 0m) =>
        ManpowerEquipmentLog.CreateOriginal(
            Guid.NewGuid(),
            projectId ?? Guid.NewGuid(),
            DateTimeOffset.Parse("2026-07-09T00:00:00+07:00"),
            Shift.Day,
            workCategoryId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            LabourType.OwnDirect,
            null,
            workerCount,
            manHours,
            overtimeHours,
            manHoursDerived: false,
            equipmentCount,
            equipmentOperatingHours,
            equipmentStandbyHours,
            "งานโครงสร้าง ชั้น 9",
            null,
            recordedByUserId ?? Guid.NewGuid(),
            DateTimeOffset.Parse("2026-07-09T18:00:00+07:00"),
            allowDuplicateOverride: false);

    [Fact]
    public void CreateOriginal_Assigns_All_Fields_And_Carries_No_Correction_Chain()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var workCategoryId = Guid.NewGuid();
        var wbsNodeId = Guid.NewGuid();
        var recordedByUserId = Guid.NewGuid();
        var logDate = DateTimeOffset.Parse("2026-07-09T00:00:00+07:00");
        var recordedAt = DateTimeOffset.Parse("2026-07-09T18:30:00+07:00");

        var log = ManpowerEquipmentLog.CreateOriginal(
            tenantId, projectId, logDate, Shift.Day, workCategoryId, wbsNodeId, null, LabourType.OwnDirect,
            null, 25, 200.00m, 20.00m, false, 2, 14.00m, 2.00m, "งานโครงสร้าง", null, recordedByUserId,
            recordedAt, allowDuplicateOverride: false);

        Assert.Equal(tenantId, log.TenantId);
        Assert.Equal(projectId, log.ProjectId);
        Assert.Equal(logDate, log.LogDate);
        Assert.Equal(Shift.Day, log.Shift);
        Assert.Equal(workCategoryId, log.WorkCategoryId);
        Assert.Equal(wbsNodeId, log.WbsNodeId);
        Assert.Null(log.ActivityId);
        Assert.Equal(LabourType.OwnDirect, log.LabourType);
        Assert.Equal(25, log.WorkerCount);
        Assert.Equal(200.00m, log.ManHours);
        Assert.Equal(20.00m, log.OvertimeHours);
        Assert.False(log.ManHoursDerived);
        Assert.Equal(2, log.EquipmentCount);
        Assert.Equal(14.00m, log.EquipmentOperatingHours);
        Assert.Equal(2.00m, log.EquipmentStandbyHours);
        Assert.Equal(recordedByUserId, log.RecordedByUserId);
        Assert.Equal(recordedAt, log.RecordedAt);
        Assert.Equal(ManpowerLogEntryKind.Original, log.EntryKind);
        Assert.Null(log.CorrectsLogId);
        Assert.Null(log.CorrectionReason);
        Assert.False(log.AllowDuplicateOverride);
    }

    [Fact]
    public void CreateOriginal_Allows_A_Meaningful_Zero_Row_Worker_Count_Zero_ManHours_Zero()
    {
        // §4.1: "งานหยุด - ไม่มีคนเข้างาน" on a day the site was shut is valid and meaningful, not
        // the same as no row at all.
        var log = CreateOriginal(workerCount: 0, manHours: 0.00m);
        Assert.Equal(0, log.WorkerCount);
        Assert.Equal(0.00m, log.ManHours);
    }

    [Fact]
    public void CreateOriginal_Rejects_An_Empty_ProjectId()
    {
        Assert.Throws<DomainException>(() => CreateOriginal(projectId: Guid.Empty));
    }

    [Fact]
    public void CreateOriginal_Rejects_An_Empty_WorkCategoryId()
    {
        Assert.Throws<DomainException>(() => CreateOriginal(workCategoryId: Guid.Empty));
    }

    [Fact]
    public void CreateOriginal_Rejects_An_Empty_RecordedByUserId()
    {
        // L-01 pattern (this task's brief): the handler is responsible for fail-closed ActorRequired
        // BEFORE ever reaching this constructor - this is the constructor's own belt-and-braces copy.
        Assert.Throws<DomainException>(() => CreateOriginal(recordedByUserId: Guid.Empty));
    }

    [Theory]
    [InlineData(25, 601.00)] // > 25 * 24.00
    public void CreateOriginal_Rejects_ManHours_Exceeding_WorkerCount_Times_24(int workerCount, double manHours)
    {
        Assert.Throws<DomainException>(() => CreateOriginal(workerCount: workerCount, manHours: (decimal)manHours));
    }

    [Fact]
    public void CreateOriginal_Allows_ManHours_Exactly_At_The_24Hour_Ceiling()
    {
        var log = CreateOriginal(workerCount: 25, manHours: 600.00m);
        Assert.Equal(600.00m, log.ManHours);
    }

    [Fact]
    public void CreateOriginal_Rejects_ManHours_Greater_Than_Zero_With_Zero_WorkerCount()
    {
        Assert.Throws<DomainException>(() => CreateOriginal(workerCount: 0, manHours: 1.00m));
    }

    [Fact]
    public void CreateOriginal_Rejects_OvertimeHours_Exceeding_ManHours()
    {
        Assert.Throws<DomainException>(() => CreateOriginal(manHours: 200.00m, overtimeHours: 201.00m));
    }

    [Fact]
    public void CreateOriginal_Allows_OvertimeHours_Equal_To_ManHours()
    {
        var log = CreateOriginal(manHours: 200.00m, overtimeHours: 200.00m);
        Assert.Equal(200.00m, log.OvertimeHours);
    }

    [Fact]
    public void CreateOriginal_Rejects_Equipment_Hours_Exceeding_EquipmentCount_Times_24()
    {
        Assert.Throws<DomainException>(() => CreateOriginal(equipmentCount: 1, equipmentOperatingHours: 20.00m, equipmentStandbyHours: 5.00m));
    }

    [Theory]
    [InlineData(-1)]
    public void CreateOriginal_Rejects_A_Negative_WorkerCount(int workerCount)
    {
        Assert.Throws<DomainException>(() => CreateOriginal(workerCount: workerCount, manHours: 0m));
    }

    [Fact]
    public void CreateOriginal_Rejects_A_Negative_ManHours()
    {
        Assert.Throws<DomainException>(() => CreateOriginal(manHours: -1.00m));
    }

    // ---- Correction chain shape (§4.7) ----

    [Fact]
    public void CreateCorrection_Requires_A_Non_Empty_CorrectsLogId()
    {
        Assert.Throws<DomainException>(() => ManpowerEquipmentLog.CreateCorrection(
            Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, "reason",
            DateTimeOffset.Parse("2026-07-09T00:00:00+07:00"), Shift.Day, Guid.NewGuid(), Guid.NewGuid(), null,
            LabourType.OwnDirect, null, 25, 200.00m, 0m, false, 0, 0m, 0m, null, null, Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void CreateCorrection_Requires_A_Non_Blank_CorrectionReason(string? blank)
    {
        Assert.Throws<DomainException>(() => ManpowerEquipmentLog.CreateCorrection(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), blank!,
            DateTimeOffset.Parse("2026-07-09T00:00:00+07:00"), Shift.Day, Guid.NewGuid(), Guid.NewGuid(), null,
            LabourType.OwnDirect, null, 25, 200.00m, 0m, false, 0, 0m, 0m, null, null, Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CreateCorrection_Builds_A_Valid_Replacement_Entry_The_Corrections_Own_Values_Govern_Completely()
    {
        var targetId = Guid.NewGuid();

        // M-13 step 3: 75 คน, 660.00 h (60h of OT omitted from the original) - the correction's own
        // values, not a patch.
        var correction = ManpowerEquipmentLog.CreateCorrection(
            Guid.NewGuid(), Guid.NewGuid(), targetId, "ลืมนับ OT 60 ชม.",
            DateTimeOffset.Parse("2026-07-10T00:00:00+07:00"), Shift.Day, Guid.NewGuid(), Guid.NewGuid(), null,
            LabourType.OwnDirect, null, 75, 660.00m, 60.00m, false, 0, 0m, 0m, null, null, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(ManpowerLogEntryKind.Correction, correction.EntryKind);
        Assert.Equal(targetId, correction.CorrectsLogId);
        Assert.Equal("ลืมนับ OT 60 ชม.", correction.CorrectionReason);
        Assert.Equal(660.00m, correction.ManHours);
    }

    [Fact]
    public void CreateRetraction_Builds_A_Valid_Voiding_Entry()
    {
        var targetId = Guid.NewGuid();

        var retraction = ManpowerEquipmentLog.CreateRetraction(
            Guid.NewGuid(), Guid.NewGuid(), targetId, "บันทึกผิดวัน",
            DateTimeOffset.Parse("2026-07-10T00:00:00+07:00"), Shift.Day, Guid.NewGuid(), Guid.NewGuid(), null,
            LabourType.OwnDirect, null, 75, 600.00m, 0m, false, 0, 0m, 0m, null, null, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(ManpowerLogEntryKind.Retraction, retraction.EntryKind);
        Assert.Equal(targetId, retraction.CorrectsLogId);
        Assert.Equal("บันทึกผิดวัน", retraction.CorrectionReason);
    }

    [Fact]
    public void An_Original_Entry_Must_Not_Carry_CorrectsLogId_Or_CorrectionReason()
    {
        // Belt-and-braces: the private constructor's own Original-branch guard, reached only via
        // reflection since CreateOriginal itself never passes these - proves the invariant is real,
        // not merely "the public factory happens not to expose it".
        var ctor = typeof(ManpowerEquipmentLog).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(c => c.GetParameters().Length > 5);

        var ex = Assert.Throws<TargetInvocationException>(() => ctor.Invoke(
        [
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, Shift.Day, Guid.NewGuid(), null, null,
            LabourType.OwnDirect, null, 1, 1m, 0m, false, 0, 0m, 0m, null, null, Guid.NewGuid(), DateTimeOffset.UtcNow,
            ManpowerLogEntryKind.Original, Guid.NewGuid(), null, false,
        ]));
        Assert.IsType<DomainException>(ex.InnerException);
    }

    [Fact]
    public void Type_Has_No_Public_Property_Setters()
    {
        var propertiesWithPublicSetters = typeof(ManpowerEquipmentLog)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetSetMethod(nonPublic: false) is not null)
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(propertiesWithPublicSetters);
    }

    [Fact]
    public void Type_Has_No_Public_Mutating_Instance_Methods()
    {
        var mutatingMethods = typeof(ManpowerEquipmentLog)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.DeclaringType == typeof(ManpowerEquipmentLog))
            .Select(m => m.Name)
            .ToList();

        Assert.Empty(mutatingMethods);
    }

    [Fact]
    public void Type_Has_No_Public_Constructor_Only_The_Three_Named_Factories_Can_Create_An_Instance()
    {
        var publicConstructors = typeof(ManpowerEquipmentLog).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(publicConstructors);
    }

    [Fact]
    public void Type_Implements_IAppendOnly()
    {
        // The structural guarantee this task's brief calls out by name: M-13's fix is the
        // AppendOnlyGuardInterceptor keying off this exact marker, not merely "no setter exists".
        Assert.True(typeof(IAppendOnly).IsAssignableFrom(typeof(ManpowerEquipmentLog)));
    }

    [Fact]
    public void Sanity_Check_The_Fixture_Helper_Itself_Constructs_Successfully()
    {
        var log = CreateOriginal();
        Assert.NotEqual(Guid.Empty, log.Id);
    }
}
