using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Eot;
using CMPlus.Application.Features.Eot.Commands.EvaluateEot;
using CMPlus.Application.Services.Cpm;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Interceptors;
using CMPlus.Integration.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Eot;

/// <summary>
/// S11-BE-02, end to end against a real (InMemory) <see cref="CmPlusDbContext"/> - unlike
/// <c>RecalculateCpmCommandHandlerIntegrationTests</c>, every write this handler makes is an ordinary
/// tracked EF Core <c>Add</c> (no raw-SQL <c>OPENJSON</c>), so this suite is NOT soft-skipped when no
/// real SQL Server is reachable (docs/perf/gantt-frontend-s6.md §3) and actually executes in this
/// session. Wires the real <see cref="EotEvaluationRepository"/>, <see cref="DailyWeatherLogRepository"/>
/// and <see cref="CpmRunHistoryReader"/> together - proving the governing-run resolution and the
/// zero-side-effect boundary (§2.1, fixture W-14) against real persistence, not just the pure
/// <c>EotEvaluator</c> (already covered fixture-by-fixture in
/// <c>CMPlus.Application.Tests.Eot.EotEvaluatorTests</c>).
/// </summary>
public class EvaluateEotCommandHandlerIntegrationTests
{
    private static readonly Guid AId = Guid.NewGuid();
    private static readonly Guid BId = Guid.NewGuid();
    private static readonly Guid CId = Guid.NewGuid();
    private static readonly Guid DId = Guid.NewGuid();

    private sealed class FixedDateTimeProvider(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FixedCurrentUserContext(Guid? userId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;

        public UserRole Role => UserRole.PM;
    }

    private static CmPlusDbContext CreateContext(
        string databaseName, FakeTenantProvider tenantProvider, Guid? actorUserId, DateTimeOffset now)
    {
        // The real interceptor trio, wired exactly like production's CmPlusDbContext registration
        // (CMPlus.Infrastructure.DependencyInjection) - without AppendOnlyGuardInterceptor this test
        // would prove nothing about IAppendOnly actually being enforced, and without
        // AuditSaveChangesInterceptor there would be nothing for SuppressPerEntityAudit to suppress.
        var options = new DbContextOptionsBuilder<CmPlusDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(
                new AuditSaveChangesInterceptor(tenantProvider, new FixedCurrentUserContext(actorUserId), new FixedDateTimeProvider(now)),
                new RowVersionSaveChangesInterceptor(),
                new AppendOnlyGuardInterceptor())
            .Options;

        return new CmPlusDbContext(options, tenantProvider);
    }

    private static DateTimeOffset D(int year, int month, int day) => new(year, month, day, 0, 0, 0, TimeSpan.FromHours(7));

    private sealed record Seed(Project Project, Activity A, Activity B, Activity C, Activity D, CpmRun Run);

    /// <summary>Network N1 (domain-rules.md §10.0) seeded against a real DbContext: Project, one
    /// default TH-6Day calendar (+ the 2026-07-28 holiday exception), four activities all in
    /// progress since before the window, and one <see cref="CpmRun"/> captured by actually running
    /// <see cref="CpmEngine.Calculate"/> - never hand-transcribed.</summary>
    private static async Task<Seed> SeedAsync(CmPlusDbContext context, Guid tenantId, Guid actorUserId, DateTimeOffset calculatedAt)
    {
        var project = Project.Create(
            tenantId, "EOT Integration Test Project", $"P-EOT-{Guid.NewGuid():N}", "Owner",
            D(2026, 1, 1), D(2026, 12, 31), bac: 1_000_000m, dataDate: D(2026, 7, 31));
        context.Projects.Add(project);

        var wbsNode = new WBSNode(tenantId, project.Id, "C1", "Civil Works", 100m);
        context.WBSNodes.Add(wbsNode);

        var start = D(2026, 6, 15);
        var a = new Activity(tenantId, wbsNode.Id, "A", "Activity A", start, start.AddDays(5), 5, 100_000m);
        var b = new Activity(tenantId, wbsNode.Id, "B", "Activity B", start, start.AddDays(3), 3, 100_000m);
        var c = new Activity(tenantId, wbsNode.Id, "C", "Activity C", start, start.AddDays(6), 6, 100_000m);
        var d = new Activity(tenantId, wbsNode.Id, "D", "Activity D", start, start.AddDays(4), 4, 100_000m);
        a.SetActuals(start, null);
        b.SetActuals(start, null);
        c.SetActuals(start, null);
        d.SetActuals(start, null);
        context.Activities.AddRange(a, b, c, d);

        var relations = new[]
        {
            new ActivityRelation(tenantId, a.Id, b.Id, RelationType.FS, 0),
            new ActivityRelation(tenantId, a.Id, c.Id, RelationType.FS, 0),
            new ActivityRelation(tenantId, b.Id, d.Id, RelationType.FS, 0),
            new ActivityRelation(tenantId, c.Id, d.Id, RelationType.FS, 0),
        };
        context.ActivityRelations.AddRange(relations);

        var calendar = new Calendar(tenantId, project.Id, "TH-6Day", WorkingDays.Weekdays | WorkingDays.Saturday, isDefault: true);
        context.Calendars.Add(calendar);
        var holiday = calendar.AddException(D(2026, 7, 28), isWorkingDay: false, "วันเฉลิมพระชนมพรรษา");
        context.CalendarExceptions.Add(holiday);

        await context.SaveChangesAsync();

        var activityInputs = new[] { new CpmActivityInput(a.Id, 5), new(b.Id, 3), new(c.Id, 6), new(d.Id, 4) };
        var relationInputs = relations.Select(r => new CpmRelationInput(r.PredecessorActivityId, r.SuccessorActivityId, r.RelationType, r.LagDays)).ToList();
        var calc = CpmEngine.Calculate(activityInputs, relationInputs);
        Assert.True(calc.IsSuccess);

        var durationById = new Dictionary<Guid, int> { [a.Id] = 5, [b.Id] = 3, [c.Id] = 6, [d.Id] = 4 };
        var run = CpmRun.Capture(
            tenantId, project.Id, calculatedAt, project.DataDate, calc.Value.ProjectDurationDays, actorUserId,
            CpmRunTrigger.Manual,
            calc.Value.Activities
                .Select(r => new CpmRunActivityInput(
                    r.ActivityId, durationById[r.ActivityId], r.EarlyStart, r.EarlyFinish, r.LateStart, r.LateFinish,
                    r.TotalFloat, r.FreeFloat, r.IsCritical))
                .ToList(),
            relationInputs.Select(r => new CpmRunRelationInput(r.PredecessorActivityId, r.SuccessorActivityId, r.RelationType, r.LagDays)).ToList());
        context.CpmRuns.Add(run);

        // Also write the live Activity.IsCritical/TotalFloat/FreeFloat back, exactly like
        // RecalculateCpmCommandHandler does alongside capturing the CpmRun - this is what makes the
        // W-14 "B's live fields are untouched by the evaluator" assertion meaningful (they must start
        // as real, non-null N1 values: B not critical, TotalFloat/FreeFloat = 3).
        var byActivityId = new Dictionary<Guid, Activity> { [a.Id] = a, [b.Id] = b, [c.Id] = c, [d.Id] = d };
        foreach (var activityResult in calc.Value.Activities)
        {
            byActivityId[activityResult.ActivityId].SetCpmResults(activityResult.IsCritical, activityResult.TotalFloat, activityResult.FreeFloat);
        }

        await context.SaveChangesAsync();

        return new Seed(project, a, b, c, d, run);
    }

    private static void AddWeatherLog(CmPlusDbContext context, Guid tenantId, Guid projectId, Guid recordedByUserId, DateTimeOffset logDate, Guid activityId)
    {
        var log = DailyWeatherLog.CreateOriginal(
            tenantId, projectId, logDate, WeatherCondition.HeavyRain, "ฝนตกหนัก", 45.00m, WeatherImpact.FullStoppage, null,
            8.00m, recordedByUserId, logDate, [activityId]);
        context.DailyWeatherLogs.Add(log);
    }

    private static EvaluateEotCommandHandler CreateHandler(CmPlusDbContext context, FakeTenantProvider tenantProvider, Guid? actorUserId, DateTimeOffset now) =>
        new(
            new EotEvaluationRepository(context, tenantProvider, new FixedCurrentUserContext(actorUserId), new FixedDateTimeProvider(now)),
            new DailyWeatherLogRepository(context),
            new CpmRunHistoryReader(context),
            tenantProvider,
            new FixedCurrentUserContext(actorUserId),
            new FixedDateTimeProvider(now));

    /// <summary>★ W-03 end to end - the flagship "everything wired together" test. 5 stoppages on B
    /// (float 3) against real persistence reproduces the same 2-day answer
    /// <c>EotEvaluatorTests.W03_...</c> proves in isolation, and additionally proves the full
    /// explainability graph (Runs/Sources/Drivers) actually persists and reloads correctly.</summary>
    [Fact]
    public async Task Handle_W03_End_To_End_Reproduces_Two_Days_And_Persists_The_Full_Explainability_Graph()
    {
        var tenantId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var calculatedAt = D(2026, 7, 1);
        var evaluatedAt = D(2026, 8, 1);

        Seed seed;
        {
            var tenantProvider = new FakeTenantProvider(tenantId);
            await using var context = CreateContext(databaseName, tenantProvider, actorUserId, calculatedAt);
            seed = await SeedAsync(context, tenantId, actorUserId, calculatedAt);

            foreach (var day in new[] { 8, 9, 10, 11, 13 })
            {
                AddWeatherLog(context, tenantId, seed.Project.Id, actorUserId, D(2026, 7, day), seed.B.Id);
            }

            await context.SaveChangesAsync();
        }

        Result<EotEvaluationDto> result;
        {
            var tenantProvider = new FakeTenantProvider(tenantId);
            await using var context = CreateContext(databaseName, tenantProvider, actorUserId, evaluatedAt);
            var handler = CreateHandler(context, tenantProvider, actorUserId, evaluatedAt);

            result = await handler.Handle(
                new EvaluateEotCommand(seed.Project.Id, D(2026, 7, 1), D(2026, 7, 31)), CancellationToken.None);
        }

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(2, result.Value.EotEligibleDays);
        Assert.Equal(EotCriticalityBasis.Contemporaneous, result.Value.CriticalityBasis);
        Assert.Equal(EotConfidence.Substantiated, result.Value.Confidence);
        Assert.False(result.Value.ConcurrencyAssessed);
        Assert.False(result.Value.EntitlementBasisAssessed);

        {
            var tenantProvider = new FakeTenantProvider(tenantId);
            await using var verify = CreateContext(databaseName, tenantProvider, actorUserId, evaluatedAt);

            var reloaded = await verify.EotEvaluations
                .Include(e => e.Runs)
                .Include(e => e.Sources)
                .Include(e => e.Drivers)
                .AsNoTracking()
                .SingleAsync(e => e.Id == result.Value.Id);

            Assert.Equal(2, reloaded.EotEligibleDays);
            Assert.Equal(15, reloaded.AsScheduledDurationDays);
            Assert.Equal(17, reloaded.ImpactedDurationDays);
            var run = Assert.Single(reloaded.Runs);
            Assert.Equal(seed.Run.Id, run.CpmRunId);
            Assert.Equal(5, reloaded.Sources.Count);
            var driver = Assert.Single(reloaded.Drivers);
            Assert.Equal(seed.B.Id, driver.ActivityId);
            Assert.Equal(5, driver.StoppageDays);
            Assert.True(driver.IsOnImpactedCriticalPath);
        }
    }

    /// <summary>★ W-11b, through the REAL <see cref="CpmRunHistoryReader"/> - unlike
    /// <c>EotEvaluatorTests.W11b_...</c> (which supplies an already-resolved governing run directly,
    /// by design, since <c>EotEvaluator</c> itself performs no I/O), this proves the handler's own
    /// governing-run resolution (<c>EvaluateEotCommandHandler</c>'s <c>asOf</c>/<c>GetGoverningRunAsync</c>
    /// wiring) really implements T2 (contemporaneous), not T1 (latest schedule), against a real
    /// database with two persisted <see cref="CpmRun"/>s either side of the stoppage date.</summary>
    [Fact]
    public async Task Handle_W11b_Resolves_The_Contemporaneous_Run_Through_The_Real_CpmRunHistoryReader()
    {
        var tenantId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var tenantProvider = new FakeTenantProvider(tenantId);

        Guid projectId, cId;
        Guid r1Id;
        {
            await using var context = CreateContext(databaseName, tenantProvider, actorUserId, D(2026, 7, 1));
            var seed = await SeedAsync(context, tenantId, actorUserId, D(2026, 7, 1)); // R1: N1, CalculatedAt 2026-07-01.
            projectId = seed.Project.Id;
            cId = seed.C.Id;
            r1Id = seed.Run.Id;

            // R2: N1' (B's duration 3->7, per W-11b/W-15), captured later on 2026-07-15 - postdates
            // the stoppage below, so it must NOT govern it.
            var activityInputs = new[] { new CpmActivityInput(seed.A.Id, 5), new(seed.B.Id, 7), new(cId, 6), new(seed.D.Id, 4) };
            var relationInputs = new[]
            {
                new CpmRelationInput(seed.A.Id, seed.B.Id, RelationType.FS, 0),
                new CpmRelationInput(seed.A.Id, cId, RelationType.FS, 0),
                new CpmRelationInput(seed.B.Id, seed.D.Id, RelationType.FS, 0),
                new CpmRelationInput(cId, seed.D.Id, RelationType.FS, 0),
            };
            var calc = CpmEngine.Calculate(activityInputs, relationInputs);
            Assert.True(calc.IsSuccess);
            Assert.Equal(16, calc.Value.ProjectDurationDays); // N1' sanity check, per W-11b's own worked table.

            var durationById = activityInputs.ToDictionary(a => a.ActivityId, a => a.DurationDays);
            var r2 = CpmRun.Capture(
                tenantId, projectId, D(2026, 7, 15), null, calc.Value.ProjectDurationDays, actorUserId, CpmRunTrigger.Manual,
                calc.Value.Activities
                    .Select(r => new CpmRunActivityInput(r.ActivityId, durationById[r.ActivityId], r.EarlyStart, r.EarlyFinish, r.LateStart, r.LateFinish, r.TotalFloat, r.FreeFloat, r.IsCritical))
                    .ToList(),
                relationInputs.Select(r => new CpmRunRelationInput(r.PredecessorActivityId, r.SuccessorActivityId, r.RelationType, r.LagDays)).ToList());
            context.CpmRuns.Add(r2);

            AddWeatherLog(context, tenantId, projectId, actorUserId, D(2026, 7, 8), cId);
            await context.SaveChangesAsync();
        }

        await using var handlerContext = CreateContext(databaseName, tenantProvider, actorUserId, D(2026, 8, 1));
        var handler = CreateHandler(handlerContext, tenantProvider, actorUserId, D(2026, 8, 1));

        var result = await handler.Handle(
            new EvaluateEotCommand(projectId, D(2026, 7, 1), D(2026, 7, 31)), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(1, result.Value.EotEligibleDays); // T2, ruled correct - R1 governs, not R2.
        var run = Assert.Single(result.Value.Runs);
        Assert.Equal(r1Id, run.CpmRunId); // never R2, even though R2 is the "current"/latest schedule.
    }

    [Fact]
    public async Task Handle_Returns_ProjectCalendarNotConfigured_When_The_Project_Has_No_Default_Calendar()
    {
        var tenantId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var tenantProvider = new FakeTenantProvider(tenantId);

        Guid projectId;
        await using (var context = CreateContext(databaseName, tenantProvider, actorUserId, D(2026, 7, 1)))
        {
            var project = Project.Create(
                tenantId, "No Calendar Project", $"P-NOCAL-{Guid.NewGuid():N}", "Owner",
                D(2026, 1, 1), D(2026, 12, 31), bac: 100_000m, dataDate: D(2026, 7, 31));
            context.Projects.Add(project);
            await context.SaveChangesAsync();
            projectId = project.Id;

            // A CpmRun exists (so the calendar gate, not the CpmRun gate, is what fires).
            context.CpmRuns.Add(CpmRun.Capture(tenantId, projectId, D(2026, 7, 1), null, 0, actorUserId, CpmRunTrigger.Manual, [], []));
            await context.SaveChangesAsync();
        }

        await using var handlerContext = CreateContext(databaseName, tenantProvider, actorUserId, D(2026, 8, 1));
        var handler = CreateHandler(handlerContext, tenantProvider, actorUserId, D(2026, 8, 1));

        var result = await handler.Handle(new EvaluateEotCommand(projectId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(EotErrorCodes.ProjectCalendarNotConfigured, result.Error);
    }

    [Fact]
    public async Task Handle_Returns_NoCpmRunAvailable_When_The_Project_Has_Never_Had_A_CpmRun()
    {
        var tenantId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var tenantProvider = new FakeTenantProvider(tenantId);

        Guid projectId;
        await using (var context = CreateContext(databaseName, tenantProvider, actorUserId, D(2026, 7, 1)))
        {
            var project = Project.Create(
                tenantId, "No CpmRun Project", $"P-NORUN-{Guid.NewGuid():N}", "Owner",
                D(2026, 1, 1), D(2026, 12, 31), bac: 100_000m, dataDate: D(2026, 7, 31));
            context.Projects.Add(project);
            context.Calendars.Add(new Calendar(tenantId, project.Id, "Cal", WorkingDays.Weekdays, isDefault: true));
            await context.SaveChangesAsync();
            projectId = project.Id;
        }

        await using var handlerContext = CreateContext(databaseName, tenantProvider, actorUserId, D(2026, 8, 1));
        var handler = CreateHandler(handlerContext, tenantProvider, actorUserId, D(2026, 8, 1));

        var result = await handler.Handle(new EvaluateEotCommand(projectId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(EotErrorCodes.NoCpmRunAvailable, result.Error);
    }

    /// <summary>domain-rules.md §2.1, fixture W-14 - "the evaluator writes nothing". Runs W-03's data
    /// end to end and asserts byte-equality (including <c>RowVersion</c>) on every row of every
    /// other table before/after, and that exactly one new row landed anywhere outside
    /// <c>EotEvaluation</c>/its children: the <see cref="AuditLog"/>.</summary>
    [Fact]
    public async Task Handle_W14_Writes_Only_Its_Own_Aggregate_And_Exactly_One_AuditLog_Row()
    {
        var tenantId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var calculatedAt = D(2026, 7, 1);
        var evaluatedAt = D(2026, 8, 1);

        Seed seed;
        {
            var tenantProvider = new FakeTenantProvider(tenantId);
            await using var context = CreateContext(databaseName, tenantProvider, actorUserId, calculatedAt);
            seed = await SeedAsync(context, tenantId, actorUserId, calculatedAt);

            foreach (var day in new[] { 8, 9, 10, 11, 13 })
            {
                AddWeatherLog(context, tenantId, seed.Project.Id, actorUserId, D(2026, 7, day), seed.B.Id);
            }

            await context.SaveChangesAsync();
        }

        // --- Snapshot BEFORE. ---
        string projectBefore, activitiesBefore, relationsBefore, calendarsBefore, calendarExceptionsBefore, weatherLogsBefore, cpmRunsBefore;
        int auditLogCountBefore, evaluationCountBefore;
        {
            var tenantProvider = new FakeTenantProvider(tenantId);
            await using var verify = CreateContext(databaseName, tenantProvider, actorUserId, evaluatedAt);
            projectBefore = await SerializeAllAsync(verify.Projects);
            activitiesBefore = await SerializeAllAsync(verify.Activities);
            relationsBefore = await SerializeAllAsync(verify.ActivityRelations);
            calendarsBefore = await SerializeAllAsync(verify.Calendars);
            calendarExceptionsBefore = await SerializeAllAsync(verify.CalendarExceptions);
            weatherLogsBefore = await SerializeAllAsync(verify.DailyWeatherLogs.Include(w => w.AffectedActivities));
            cpmRunsBefore = await SerializeAllAsync(verify.CpmRuns.Include(r => r.Activities).Include(r => r.Relations));
            auditLogCountBefore = await verify.AuditLogs.CountAsync();
            evaluationCountBefore = await verify.EotEvaluations.CountAsync();

            // domain-rules.md §2.1's own literal table list - all four are untouched by anything
            // this feature does (no code path here can even reference them), asserted explicitly
            // rather than merely assumed.
            Assert.Equal(0, await verify.ProjectFinanceLedgers.CountAsync());
            Assert.Equal(0, await verify.EvmPeriodSnapshots.CountAsync());
            Assert.Equal(0, await verify.VariationOrders.CountAsync());
            Assert.Equal(0, await verify.PaymentCertificates.CountAsync());

            // Sanity check the fixture itself is what W-03 expects before mutating anything.
            var b = await verify.Activities.AsNoTracking().SingleAsync(a => a.Id == seed.B.Id);
            Assert.False(b.IsCritical);
            Assert.Equal(3, b.TotalFloat);
        }

        Result<EotEvaluationDto> result;
        {
            var tenantProvider = new FakeTenantProvider(tenantId);
            await using var context = CreateContext(databaseName, tenantProvider, actorUserId, evaluatedAt);
            var handler = CreateHandler(context, tenantProvider, actorUserId, evaluatedAt);
            result = await handler.Handle(
                new EvaluateEotCommand(seed.Project.Id, D(2026, 7, 1), D(2026, 7, 31)), CancellationToken.None);
        }

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(2, result.Value.EotEligibleDays); // sanity: this really is W-03's impacted-network case.

        // --- Snapshot AFTER - byte-equal to BEFORE on every one of these tables. ---
        {
            var tenantProvider = new FakeTenantProvider(tenantId);
            await using var verify = CreateContext(databaseName, tenantProvider, actorUserId, evaluatedAt);

            Assert.Equal(projectBefore, await SerializeAllAsync(verify.Projects));
            Assert.Equal(activitiesBefore, await SerializeAllAsync(verify.Activities));
            Assert.Equal(relationsBefore, await SerializeAllAsync(verify.ActivityRelations));
            Assert.Equal(calendarsBefore, await SerializeAllAsync(verify.Calendars));
            Assert.Equal(calendarExceptionsBefore, await SerializeAllAsync(verify.CalendarExceptions));
            Assert.Equal(weatherLogsBefore, await SerializeAllAsync(verify.DailyWeatherLogs.Include(w => w.AffectedActivities)));
            Assert.Equal(cpmRunsBefore, await SerializeAllAsync(verify.CpmRuns.Include(r => r.Activities).Include(r => r.Relations)));
            Assert.Equal(0, await verify.ProjectFinanceLedgers.CountAsync());
            Assert.Equal(0, await verify.EvmPeriodSnapshots.CountAsync());
            Assert.Equal(0, await verify.VariationOrders.CountAsync());
            Assert.Equal(0, await verify.PaymentCertificates.CountAsync());

            // Specifically: B's IsCritical/TotalFloat are exactly as before, even though the impacted
            // network (computed only in memory) makes B critical with float 0 (§2.1, fixture W-14).
            var b = await verify.Activities.AsNoTracking().SingleAsync(a => a.Id == seed.B.Id);
            Assert.False(b.IsCritical);
            Assert.Equal(3, b.TotalFloat);
            Assert.Equal(3, b.FreeFloat);

            // Exactly one new AuditLog row, and exactly one new EotEvaluation - nothing else, in any table.
            Assert.Equal(1, await verify.AuditLogs.CountAsync() - auditLogCountBefore);
            Assert.Equal(1, await verify.EotEvaluations.CountAsync() - evaluationCountBefore);

            var newAuditLog = await verify.AuditLogs.AsNoTracking().OrderByDescending(a => a.Timestamp).FirstAsync();
            Assert.Equal(nameof(EotEvaluation), newAuditLog.EntityName);
            Assert.Equal(result.Value.Id, newAuditLog.EntityId);
            Assert.Equal(AuditAction.Created, newAuditLog.Action);
        }
    }

    /// <summary>Order-independent, whole-table byte-equality snapshot: serializes every row's every
    /// scalar property (via reflection, so a future field addition is automatically covered) as JSON,
    /// sorted by <c>Id</c> so insertion order cannot mask a real difference or hide a real one.</summary>
    private static async Task<string> SerializeAllAsync<T>(IQueryable<T> set) where T : class
    {
        var rows = await set.AsNoTracking().ToListAsync();
        var serialized = rows
            .Select(r => System.Text.Json.JsonSerializer.Serialize(r, new System.Text.Json.JsonSerializerOptions { IncludeFields = false }))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        return string.Join("\n", serialized);
    }
}
