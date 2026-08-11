using System.Reflection;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Tests.Entities;

/// <summary>
/// ADR-0019 (domain-rules.md weather-eot §4.3): <see cref="CpmRun"/> is the append-only history a
/// stoppage-date criticality question needs, since <see cref="Activity.IsCritical"/>/
/// <see cref="Activity.TotalFloat"/>/<see cref="Activity.FreeFloat"/> are overwritten on every
/// recalculation with no history. Structural immutability (no public mutator anywhere) is verified
/// here the same way <c>DailyWeatherLogTests</c> already established for the first entity built to
/// this exact pattern; persistence-layer enforcement (the actual "cannot be rewritten through an
/// ordinary DbContext" guarantee) is proven separately in
/// <c>CMPlus.Integration.Tests.Persistence.AppendOnlyGuardInterceptorTests</c>.
/// </summary>
public class CpmRunTests
{
    private static readonly Guid ActivityA = Guid.NewGuid();
    private static readonly Guid ActivityB = Guid.NewGuid();

    private static CpmRunActivityInput CreateActivityInput(Guid? activityId = null, int durationDays = 5) => new(
        activityId ?? ActivityA, durationDays, EarlyStart: 0, EarlyFinish: durationDays, LateStart: 0, LateFinish: durationDays,
        TotalFloat: 0, FreeFloat: 0, IsCritical: true);

    private static CpmRunRelationInput CreateRelationInput(Guid? predecessorId = null, Guid? successorId = null) => new(
        predecessorId ?? ActivityA, successorId ?? ActivityB, RelationType.FS, LagDays: 0);

    private static CpmRun CreateRun(
        Guid? tenantId = null,
        Guid? projectId = null,
        DateTimeOffset? calculatedAt = null,
        DateTimeOffset? dataDate = null,
        int projectDurationDays = 15,
        Guid? triggeredByUserId = null,
        CpmRunTrigger trigger = CpmRunTrigger.Manual,
        IReadOnlyCollection<CpmRunActivityInput>? activities = null,
        IReadOnlyCollection<CpmRunRelationInput>? relations = null) =>
        CpmRun.Capture(
            tenantId ?? Guid.NewGuid(),
            projectId ?? Guid.NewGuid(),
            calculatedAt ?? DateTimeOffset.Parse("2026-08-10T09:00:00Z"),
            dataDate ?? DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            projectDurationDays,
            triggeredByUserId ?? Guid.NewGuid(),
            trigger,
            activities ?? [CreateActivityInput()],
            relations ?? []);

    [Fact]
    public void Capture_Assigns_All_Fields()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var calculatedAt = DateTimeOffset.Parse("2026-08-10T09:15:00+07:00");
        var dataDate = DateTimeOffset.Parse("2026-08-05T00:00:00+07:00");
        var triggeredByUserId = Guid.NewGuid();

        var run = CreateRun(
            tenantId, projectId, calculatedAt, dataDate, projectDurationDays: 15, triggeredByUserId, CpmRunTrigger.Manual);

        Assert.Equal(tenantId, run.TenantId);
        Assert.Equal(projectId, run.ProjectId);
        Assert.Equal(calculatedAt, run.CalculatedAt);
        Assert.Equal(dataDate, run.DataDate);
        Assert.Equal(15, run.ProjectDurationDays);
        Assert.Equal(triggeredByUserId, run.TriggeredByUserId);
        Assert.Equal(CpmRunTrigger.Manual, run.Trigger);
    }

    [Fact]
    public void Capture_Allows_DataDate_To_Be_Null()
    {
        // Not every project has a DataDate populated - never defaulted (same discipline as
        // DailyWeatherLog.RainfallMm). Calls Capture directly (not the CreateRun helper, whose `??`
        // defaulting cannot distinguish "explicitly null" from "omitted").
        var run = CpmRun.Capture(
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.Parse("2026-08-10T09:00:00Z"), dataDate: null,
            projectDurationDays: 15, Guid.NewGuid(), CpmRunTrigger.Manual, [CreateActivityInput()], []);

        Assert.Null(run.DataDate);
    }

    [Fact]
    public void Capture_Allows_TriggeredByUserId_To_Be_Null_For_A_System_Trigger()
    {
        // Forward-compatibility for a not-yet-wired System/background trigger with no human actor -
        // see CpmRun.TriggeredByUserId's own remarks on why this must never be a fabricated id
        // instead. RecalculateCpmCommandHandler's own Manual path fails closed rather than ever
        // calling Capture with a null actor - this proves the entity itself does not forbid it, for
        // when a real System caller exists. Calls Capture directly for the same reason as the test
        // above.
        var run = CpmRun.Capture(
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.Parse("2026-08-10T09:00:00Z"),
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"), projectDurationDays: 15, triggeredByUserId: null,
            CpmRunTrigger.System, [CreateActivityInput()], []);

        Assert.Null(run.TriggeredByUserId);
        Assert.Equal(CpmRunTrigger.System, run.Trigger);
    }

    [Fact]
    public void Capture_Rejects_An_Empty_ProjectId()
    {
        Assert.Throws<DomainException>(() => CreateRun(projectId: Guid.Empty));
    }

    [Fact]
    public void Capture_Rejects_A_Negative_ProjectDurationDays()
    {
        Assert.Throws<DomainException>(() => CreateRun(projectDurationDays: -1));
    }

    [Fact]
    public void Capture_Allows_Zero_Activities_And_Zero_Relations_For_An_Empty_Project()
    {
        var run = CreateRun(projectDurationDays: 0, activities: [], relations: []);

        Assert.Empty(run.Activities);
        Assert.Empty(run.Relations);
        Assert.Equal(0, run.ProjectDurationDays);
    }

    [Fact]
    public void Capture_Builds_One_CpmRunActivity_Row_Per_Input_Owned_By_This_Run()
    {
        var activityId = Guid.NewGuid();
        var input = new CpmRunActivityInput(
            activityId, DurationDays: 6, EarlyStart: 5, EarlyFinish: 11, LateStart: 5, LateFinish: 11,
            TotalFloat: 0, FreeFloat: 0, IsCritical: true);
        var run = CreateRun(activities: [input], relations: []);

        var runActivity = Assert.Single(run.Activities);
        Assert.Equal(run.Id, runActivity.CpmRunId);
        Assert.Equal(run.TenantId, runActivity.TenantId);
        Assert.Equal(activityId, runActivity.ActivityId);
        Assert.Equal(6, runActivity.DurationDays);
        Assert.Equal(5, runActivity.EarlyStart);
        Assert.Equal(11, runActivity.EarlyFinish);
        Assert.Equal(5, runActivity.LateStart);
        Assert.Equal(11, runActivity.LateFinish);
        Assert.Equal(0, runActivity.TotalFloat);
        Assert.Equal(0, runActivity.FreeFloat);
        Assert.True(runActivity.IsCritical);
    }

    [Fact]
    public void Capture_Builds_One_CpmRunRelation_Row_Per_Input_Owned_By_This_Run()
    {
        var predecessorId = Guid.NewGuid();
        var successorId = Guid.NewGuid();
        var input = new CpmRunRelationInput(predecessorId, successorId, RelationType.SS, LagDays: 2);
        var run = CreateRun(
            activities: [CreateActivityInput(predecessorId), CreateActivityInput(successorId)],
            relations: [input]);

        var runRelation = Assert.Single(run.Relations);
        Assert.Equal(run.Id, runRelation.CpmRunId);
        Assert.Equal(run.TenantId, runRelation.TenantId);
        Assert.Equal(predecessorId, runRelation.PredecessorActivityId);
        Assert.Equal(successorId, runRelation.SuccessorActivityId);
        Assert.Equal(RelationType.SS, runRelation.RelationType);
        Assert.Equal(2, runRelation.LagDays);
    }

    [Fact]
    public void Capture_Rejects_A_Relation_Input_That_Relates_An_Activity_To_Itself()
    {
        var selfId = Guid.NewGuid();
        Assert.Throws<DomainException>(() => CreateRun(
            activities: [CreateActivityInput(selfId)],
            relations: [CreateRelationInput(selfId, selfId)]));
    }

    [Fact]
    public void Capture_Rejects_An_Activity_Input_With_An_Empty_ActivityId()
    {
        Assert.Throws<DomainException>(() => CreateRun(activities: [CreateActivityInput(Guid.Empty)]));
    }

    [Fact]
    public void Capture_Rejects_An_Activity_Input_With_Negative_DurationDays()
    {
        Assert.Throws<DomainException>(() => CreateRun(activities: [CreateActivityInput(durationDays: -1)]));
    }

    // ---- Structural immutability (mirrors DailyWeatherLogTests' identical proof) ----

    [Fact]
    public void Type_Has_No_Public_Property_Setters()
    {
        var propertiesWithPublicSetters = typeof(CpmRun)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetSetMethod(nonPublic: false) is not null)
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(propertiesWithPublicSetters);
    }

    [Fact]
    public void Type_Has_No_Public_Mutating_Instance_Methods()
    {
        var mutatingMethods = typeof(CpmRun)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.DeclaringType == typeof(CpmRun))
            .Select(m => m.Name)
            .ToList();

        Assert.Empty(mutatingMethods);
    }

    [Fact]
    public void Type_Has_No_Public_Constructor_Only_Capture_Can_Create_An_Instance()
    {
        var publicConstructors = typeof(CpmRun).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(publicConstructors);
    }

    [Fact]
    public void Type_Implements_IAppendOnly_And_ITenantOwned()
    {
        // The structural guarantee this task's brief calls out by name: AppendOnlyGuardInterceptor
        // keys off this exact marker, not merely "no setter exists" - and every entity here carries
        // TenantId under the ambient global query filter (ADR-0002), never IgnoreQueryFilters.
        Assert.True(typeof(IAppendOnly).IsAssignableFrom(typeof(CpmRun)));
        Assert.True(typeof(ITenantOwned).IsAssignableFrom(typeof(CpmRun)));
    }

    [Fact]
    public void RunActivity_Type_Has_No_Public_Property_Setters_Or_Mutating_Methods_And_Implements_IAppendOnly()
    {
        var propertiesWithPublicSetters = typeof(CpmRunActivity)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetSetMethod(nonPublic: false) is not null)
            .Select(p => p.Name)
            .ToList();
        Assert.Empty(propertiesWithPublicSetters);

        var mutatingMethods = typeof(CpmRunActivity)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.DeclaringType == typeof(CpmRunActivity))
            .Select(m => m.Name)
            .ToList();
        Assert.Empty(mutatingMethods);

        Assert.True(typeof(IAppendOnly).IsAssignableFrom(typeof(CpmRunActivity)));
        Assert.True(typeof(ITenantOwned).IsAssignableFrom(typeof(CpmRunActivity)));
    }

    [Fact]
    public void RunActivity_Has_No_Public_Constructor_Only_CpmRun_Can_Create_One()
    {
        var publicConstructors = typeof(CpmRunActivity).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(publicConstructors);
    }

    [Fact]
    public void RunRelation_Type_Has_No_Public_Property_Setters_Or_Mutating_Methods_And_Implements_IAppendOnly()
    {
        var propertiesWithPublicSetters = typeof(CpmRunRelation)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetSetMethod(nonPublic: false) is not null)
            .Select(p => p.Name)
            .ToList();
        Assert.Empty(propertiesWithPublicSetters);

        var mutatingMethods = typeof(CpmRunRelation)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.DeclaringType == typeof(CpmRunRelation))
            .Select(m => m.Name)
            .ToList();
        Assert.Empty(mutatingMethods);

        Assert.True(typeof(IAppendOnly).IsAssignableFrom(typeof(CpmRunRelation)));
        Assert.True(typeof(ITenantOwned).IsAssignableFrom(typeof(CpmRunRelation)));
    }

    [Fact]
    public void RunRelation_Has_No_Public_Constructor_Only_CpmRun_Can_Create_One()
    {
        var publicConstructors = typeof(CpmRunRelation).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(publicConstructors);
    }

    [Fact]
    public void Sanity_Check_The_Fixture_Helper_Itself_Constructs_Successfully()
    {
        var run = CreateRun();
        Assert.NotEqual(Guid.Empty, run.Id);
    }
}
