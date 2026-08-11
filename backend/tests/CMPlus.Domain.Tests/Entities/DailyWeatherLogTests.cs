using System.Reflection;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Tests.Entities;

/// <summary>
/// S11-BE-01 (US-11.1): <see cref="DailyWeatherLog"/> is immutable legal evidence - verified
/// structurally here (no public mutating method, no public property setter, the same discipline
/// <c>ActualCostEntryTests</c>/<c>ApprovalActionTests</c> already established for the other
/// <see cref="IAppendOnly"/> entities), plus construction/validation and correction-chain-shape
/// coverage per <c>docs/specs/weather-eot/domain-rules.md</c> §8. Persistence-layer enforcement (the
/// actual "cannot be rewritten through an ordinary DbContext" guarantee) is proven separately in
/// <c>CMPlus.Integration.Tests.Persistence.AppendOnlyGuardInterceptorTests</c>.
/// </summary>
public class DailyWeatherLogTests
{
    private static DailyWeatherLog CreateOriginal(
        Guid? projectId = null,
        Guid? recordedByUserId = null,
        WeatherCondition condition = WeatherCondition.HeavyRain,
        string? conditionNote = "ฝนตกหนัก",
        decimal? rainfallMm = 42.5m,
        WeatherImpact impact = WeatherImpact.FullStoppage,
        string? impactNote = "หยุดเทคอนกรีตโซน B ครึ่งวัน",
        decimal? hoursLost = 8.00m,
        IReadOnlyCollection<Guid>? affectedActivityIds = null) =>
        DailyWeatherLog.CreateOriginal(
            Guid.NewGuid(),
            projectId ?? Guid.NewGuid(),
            DateTimeOffset.Parse("2026-07-11T00:00:00+07:00"),
            condition,
            conditionNote,
            rainfallMm,
            impact,
            impactNote,
            hoursLost,
            recordedByUserId ?? Guid.NewGuid(),
            DateTimeOffset.Parse("2026-07-11T18:00:00+07:00"),
            affectedActivityIds ?? []);

    [Fact]
    public void CreateOriginal_Assigns_All_Fields_And_Carries_No_Correction_Chain()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var recordedByUserId = Guid.NewGuid();
        var logDate = DateTimeOffset.Parse("2026-07-11T00:00:00+07:00");
        var recordedAt = DateTimeOffset.Parse("2026-07-11T18:30:00+07:00");
        var activityId1 = Guid.NewGuid();
        var activityId2 = Guid.NewGuid();

        var log = DailyWeatherLog.CreateOriginal(
            tenantId, projectId, logDate, WeatherCondition.Storm, "ฝนตกหนักมาก", 61.00m,
            WeatherImpact.FullStoppage, "หยุดงานภายนอกทั้งวัน", 8.00m, recordedByUserId, recordedAt,
            [activityId1, activityId2]);

        Assert.Equal(tenantId, log.TenantId);
        Assert.Equal(projectId, log.ProjectId);
        Assert.Equal(logDate, log.LogDate);
        Assert.Equal(WeatherCondition.Storm, log.Condition);
        Assert.Equal("ฝนตกหนักมาก", log.ConditionNote);
        Assert.Equal(61.00m, log.RainfallMm);
        Assert.Equal(WeatherImpact.FullStoppage, log.Impact);
        Assert.Equal("หยุดงานภายนอกทั้งวัน", log.ImpactNote);
        Assert.Equal(8.00m, log.HoursLost);
        Assert.True(log.WorkStoppage);
        Assert.Equal(recordedByUserId, log.RecordedByUserId);
        Assert.Equal(recordedAt, log.RecordedAt);
        Assert.Equal(WeatherLogEntryKind.Original, log.EntryKind);
        Assert.Null(log.CorrectsWeatherLogId);
        Assert.Null(log.CorrectionReason);
        Assert.Equal([activityId1, activityId2], log.AffectedActivities.Select(a => a.ActivityId));
    }

    [Fact]
    public void WorkStoppage_Is_Derived_From_Impact_Never_Independently_Settable()
    {
        Assert.False(CreateOriginal(impact: WeatherImpact.NoImpact, hoursLost: null, impactNote: null).WorkStoppage);
        Assert.True(CreateOriginal(impact: WeatherImpact.PartialStoppage, hoursLost: 4.00m).WorkStoppage);
        Assert.True(CreateOriginal(impact: WeatherImpact.FullStoppage, hoursLost: 8.00m).WorkStoppage);
    }

    [Fact]
    public void CreateOriginal_Rejects_An_Empty_ProjectId()
    {
        Assert.Throws<DomainException>(() => CreateOriginal(projectId: Guid.Empty));
    }

    [Fact]
    public void CreateOriginal_Rejects_An_Empty_RecordedByUserId()
    {
        // L-01 pattern (this task's brief): the handler is responsible for fail-closed ActorRequired
        // BEFORE ever reaching this constructor - this is the constructor's own belt-and-braces copy
        // of the same guard, mirroring VariationOrder's identical CreatedByUserId check.
        Assert.Throws<DomainException>(() => CreateOriginal(recordedByUserId: Guid.Empty));
    }

    [Fact]
    public void CreateOriginal_Rejects_A_Negative_RainfallMm()
    {
        Assert.Throws<DomainException>(() => CreateOriginal(rainfallMm: -0.01m));
    }

    [Theory]
    [InlineData(42.555)]
    [InlineData(1.001)]
    public void CreateOriginal_Rejects_A_RainfallMm_With_More_Than_Two_Decimal_Places(double rainfallMm)
    {
        Assert.Throws<DomainException>(() => CreateOriginal(rainfallMm: (decimal)rainfallMm));
    }

    [Fact]
    public void CreateOriginal_Allows_RainfallMm_To_Be_Null_Genuinely_Not_Measured()
    {
        // domain-rules.md §8.1: "NULL = not measured, never 0" - a distinct, legitimate value from 0.
        var log = CreateOriginal(rainfallMm: null);
        Assert.Null(log.RainfallMm);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(24.01)]
    public void CreateOriginal_Rejects_HoursLost_Outside_The_0_To_24_Range(double hoursLost)
    {
        Assert.Throws<DomainException>(() => CreateOriginal(hoursLost: (decimal)hoursLost));
    }

    [Fact]
    public void CreateOriginal_Requires_HoursLost_When_Impact_Is_Not_NoImpact()
    {
        // domain-rules.md §3.4's own text: "HoursLost is required ... when Impact <> NoImpact".
        Assert.Throws<DomainException>(() => CreateOriginal(impact: WeatherImpact.PartialStoppage, hoursLost: null));
    }

    [Fact]
    public void CreateOriginal_Accepts_Zero_Impact_With_No_HoursLost_On_A_No_Impact_Day()
    {
        // A day with no rain, no stoppage, still gets logged (the prototype's own fixture data
        // records a full row every day regardless of impact) - NoImpact + null HoursLost is the
        // legitimate "nothing happened" case, not an error.
        var log = CreateOriginal(rainfallMm: 0m, impact: WeatherImpact.NoImpact, impactNote: null, hoursLost: null);

        Assert.Equal(0m, log.RainfallMm);
        Assert.Equal(WeatherImpact.NoImpact, log.Impact);
        Assert.False(log.WorkStoppage);
        Assert.Null(log.HoursLost);
    }

    [Fact]
    public void CreateOriginal_Allows_A_Stoppage_Day_With_No_Affected_Activities_Tagged()
    {
        // Deliberately permitted (DailyWeatherLog's own class remarks): whether an untagged
        // site-wide stoppage should be treated as affecting every schedulable activity is an EOT
        // rule this task does not invent - the log itself must not forbid the combination.
        var log = CreateOriginal(impact: WeatherImpact.FullStoppage, hoursLost: 8.00m, affectedActivityIds: []);

        Assert.True(log.WorkStoppage);
        Assert.Empty(log.AffectedActivities);
    }

    [Fact]
    public void CreateOriginal_Builds_One_AffectedActivity_Row_Per_Id_Owned_By_This_Log()
    {
        var activityId = Guid.NewGuid();
        var log = CreateOriginal(affectedActivityIds: [activityId]);

        var affected = Assert.Single(log.AffectedActivities);
        Assert.Equal(log.Id, affected.DailyWeatherLogId);
        Assert.Equal(activityId, affected.ActivityId);
        Assert.Equal(log.TenantId, affected.TenantId);
    }

    // ---- Correction chain shape (domain-rules.md §8.1/§8.2) ----

    [Fact]
    public void CreateCorrection_Requires_A_Non_Empty_CorrectsWeatherLogId()
    {
        Assert.Throws<DomainException>(() => DailyWeatherLog.CreateCorrection(
            Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, "reason",
            DateTimeOffset.Parse("2026-07-11T00:00:00+07:00"), WeatherCondition.HeavyRain, null, 42.5m,
            WeatherImpact.FullStoppage, null, 8.00m, Guid.NewGuid(), DateTimeOffset.UtcNow, []));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void CreateCorrection_Requires_A_Non_Blank_CorrectionReason(string? blank)
    {
        Assert.Throws<DomainException>(() => DailyWeatherLog.CreateCorrection(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), blank!,
            DateTimeOffset.Parse("2026-07-11T00:00:00+07:00"), WeatherCondition.HeavyRain, null, 42.5m,
            WeatherImpact.FullStoppage, null, 8.00m, Guid.NewGuid(), DateTimeOffset.UtcNow, []));
    }

    [Fact]
    public void CreateCorrection_Builds_A_Valid_Replacement_Entry_The_Corrections_Own_Values_Govern_Completely()
    {
        var targetId = Guid.NewGuid();

        var correction = DailyWeatherLog.CreateCorrection(
            Guid.NewGuid(), Guid.NewGuid(), targetId, "ตรวจใบบันทึกกะแล้ว หยุดจริง 3 ชั่วโมง",
            DateTimeOffset.Parse("2026-07-08T00:00:00+07:00"), WeatherCondition.HeavyRain, null, 45.0m,
            WeatherImpact.PartialStoppage, null, 3.00m, Guid.NewGuid(), DateTimeOffset.UtcNow, []);

        Assert.Equal(WeatherLogEntryKind.Correction, correction.EntryKind);
        Assert.Equal(targetId, correction.CorrectsWeatherLogId);
        Assert.Equal("ตรวจใบบันทึกกะแล้ว หยุดจริง 3 ชั่วโมง", correction.CorrectionReason);
        Assert.Equal(3.00m, correction.HoursLost);
    }

    [Fact]
    public void CreateRetraction_Builds_A_Valid_Voiding_Entry()
    {
        var targetId = Guid.NewGuid();

        var retraction = DailyWeatherLog.CreateRetraction(
            Guid.NewGuid(), Guid.NewGuid(), targetId, "บันทึกผิดวัน",
            DateTimeOffset.Parse("2026-07-08T00:00:00+07:00"), WeatherCondition.HeavyRain, null, 45.0m,
            WeatherImpact.PartialStoppage, null, 3.00m, Guid.NewGuid(), DateTimeOffset.UtcNow, []);

        Assert.Equal(WeatherLogEntryKind.Retraction, retraction.EntryKind);
        Assert.Equal(targetId, retraction.CorrectsWeatherLogId);
        Assert.Equal("บันทึกผิดวัน", retraction.CorrectionReason);
    }

    [Fact]
    public void Type_Has_No_Public_Property_Setters()
    {
        var propertiesWithPublicSetters = typeof(DailyWeatherLog)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetSetMethod(nonPublic: false) is not null)
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(propertiesWithPublicSetters);
    }

    [Fact]
    public void Type_Has_No_Public_Mutating_Instance_Methods()
    {
        // IsSpecialName excludes property get_/set_ accessors and operator overloads; the three
        // static factories are not instance methods, so they do not appear here either - anything
        // left declared directly on DailyWeatherLog itself (not inherited from Entity/object) would
        // be a mutator - there must be none (append-only, corrections are new rows).
        var mutatingMethods = typeof(DailyWeatherLog)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.DeclaringType == typeof(DailyWeatherLog))
            .Select(m => m.Name)
            .ToList();

        Assert.Empty(mutatingMethods);
    }

    [Fact]
    public void Type_Has_No_Public_Constructor_Only_The_Three_Named_Factories_Can_Create_An_Instance()
    {
        var publicConstructors = typeof(DailyWeatherLog).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(publicConstructors);
    }

    [Fact]
    public void Type_Implements_IAppendOnly()
    {
        // The structural guarantee this task's brief calls out by name: M-01's fix is the
        // AppendOnlyGuardInterceptor keying off this exact marker, not merely "no setter exists".
        Assert.True(typeof(IAppendOnly).IsAssignableFrom(typeof(DailyWeatherLog)));
    }

    [Fact]
    public void AffectedActivity_Type_Has_No_Public_Property_Setters_Or_Mutating_Methods()
    {
        var propertiesWithPublicSetters = typeof(DailyWeatherLogActivity)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetSetMethod(nonPublic: false) is not null)
            .Select(p => p.Name)
            .ToList();
        Assert.Empty(propertiesWithPublicSetters);

        var mutatingMethods = typeof(DailyWeatherLogActivity)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.DeclaringType == typeof(DailyWeatherLogActivity))
            .Select(m => m.Name)
            .ToList();
        Assert.Empty(mutatingMethods);

        Assert.True(typeof(IAppendOnly).IsAssignableFrom(typeof(DailyWeatherLogActivity)));
    }

    [Fact]
    public void AffectedActivity_Has_No_Public_Constructor_Only_DailyWeatherLog_Can_Create_One()
    {
        // Mirrors ActivityProgressLog's "internal constructor" structural guarantee - Application/
        // Infrastructure code outside CMPlus.Domain cannot call `new DailyWeatherLogActivity(...)`
        // directly, only DailyWeatherLog's own factories can.
        var publicConstructors = typeof(DailyWeatherLogActivity)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.Empty(publicConstructors);
    }

    [Fact]
    public void Sanity_Check_The_Fixture_Helper_Itself_Constructs_Successfully()
    {
        var log = CreateOriginal();
        Assert.NotEqual(Guid.Empty, log.Id);
    }
}
