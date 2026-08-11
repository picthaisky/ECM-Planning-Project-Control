using System.Reflection;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Persistence;

/// <summary>
/// S1-BE-03 DoD: (a) a TenantId set on an entity from any source other than
/// <c>ITenantProvider</c> - e.g. one that originated from a client-supplied payload - is
/// overwritten server-side on insert; (b) the global query filter means a tenant only ever sees
/// its own rows, automatically, with no per-query predicate.
/// </summary>
public class TenantIsolationTests
{
    [Fact]
    public async Task SaveChanges_Overwrites_A_Payload_Supplied_TenantId_With_The_Current_Tenant()
    {
        var correctTenantId = Guid.NewGuid();
        var attackerSuppliedTenantId = Guid.NewGuid(); // e.g. forged in a request payload

        var factory = new TestDbContextFactory(correctTenantId);

        using (var context = factory.CreateContext())
        {
            // Simulate a handler bug: the entity is constructed with a TenantId that did NOT come
            // from ITenantProvider (e.g. it leaked in from a DTO). The Project constructor here
            // stands in for "whatever value the entity happens to carry before SaveChanges".
            var project = Project.Create(
                attackerSuppliedTenantId, "P", "C", "O",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(6),
                bac: 100m, dataDate: DateTimeOffset.UtcNow);

            context.Projects.Add(project);
            await context.SaveChangesAsync();
        }

        using (var verifyContext = factory.CreateContext())
        {
            var stored = await verifyContext.Projects
                .IgnoreQueryFilters()
                .SingleAsync();

            Assert.Equal(correctTenantId, stored.TenantId);
            Assert.NotEqual(attackerSuppliedTenantId, stored.TenantId);
        }
    }

    [Fact]
    public async Task Global_Query_Filter_Hides_Other_Tenants_Rows()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var factory = new TestDbContextFactory(tenantA);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(Project.Create(
                tenantA, "Project A", "A", "Owner",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(6), 100m, DateTimeOffset.UtcNow));
            await seedContext.SaveChangesAsync();
        }

        factory.TenantProvider.TenantId = tenantB;

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(Project.Create(
                tenantB, "Project B", "B", "Owner",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(6), 200m, DateTimeOffset.UtcNow));
            await seedContext.SaveChangesAsync();
        }

        // Query as tenant A: only Project A is visible, automatically.
        factory.TenantProvider.TenantId = tenantA;
        using (var readContext = factory.CreateContext())
        {
            var visible = await readContext.Projects.ToListAsync();

            Assert.Single(visible);
            Assert.Equal("Project A", visible[0].Name);
        }

        // Query as tenant B: only Project B is visible.
        factory.TenantProvider.TenantId = tenantB;
        using (var readContext = factory.CreateContext())
        {
            var visible = await readContext.Projects.ToListAsync();

            Assert.Single(visible);
            Assert.Equal("Project B", visible[0].Name);
        }

        // Bypassing the filter (an explicitly grep-able, audited escape hatch) proves both rows
        // really exist - the filter is hiding them, not the data being absent.
        using (var adminContext = factory.CreateContext())
        {
            var all = await adminContext.Projects.IgnoreQueryFilters().ToListAsync();
            Assert.Equal(2, all.Count);
        }
    }

    [Fact]
    public async Task Tenant_Entity_Itself_Is_Not_Query_Filtered()
    {
        var factory = new TestDbContextFactory(Guid.NewGuid());

        using (var context = factory.CreateContext())
        {
            context.Tenants.Add(new Tenant("Acme"));
            context.Tenants.Add(new Tenant("Beta Corp"));
            await context.SaveChangesAsync();
        }

        using (var readContext = factory.CreateContext())
        {
            var tenants = await readContext.Tenants.ToListAsync();

            // Both tenants are visible regardless of "current tenant" - Tenant is the
            // multi-tenancy root, explicitly exempt from scoping.
            Assert.Equal(2, tenants.Count);
        }
    }

    // ------------------------------------------------------------------------------------
    // S1-QA-01: the two tests above (Global_Query_Filter_Hides_Other_Tenants_Rows and
    // SaveChanges_Overwrites_A_Payload_Supplied_TenantId_With_The_Current_Tenant) only ever
    // covered Project. The DoD explicitly requires coverage to be "parameterized เพื่อไม่
    // ตกหล่นเมื่อเพิ่มตารางใหม่" (parameterized so a future new table doesn't silently fall
    // outside coverage) - the two [Theory] tests below close that gap by reflecting over
    // CmPlusDbContext's own built model for every CLR type implementing ITenantOwned, the same
    // way CmPlusDbContext.ApplyTenantQueryFilters itself discovers which entities need the
    // filter. A newly-added ITenantOwned entity therefore becomes a new theory case
    // automatically; if nobody has registered a fixture for it yet, the theory fails loudly
    // (see CreateFixture) instead of the type quietly having zero isolation coverage.
    // ------------------------------------------------------------------------------------

    /// <summary>All rows that must be persisted for one theory case - includes any parent row a
    /// real FK constraint requires. <see cref="CalendarException"/> and
    /// <see cref="ActivityProgressLog"/> both have <c>internal</c> constructors (only
    /// constructible via <see cref="Calendar.AddException"/> / <see cref="Activity.RecordProgress"/>
    /// respectively), so their fixture also carries the owning aggregate that produced them.</summary>
    private sealed record EntityFixture(IReadOnlyList<ITenantOwned> AllEntitiesToPersist);

    /// <summary>
    /// One factory per <see cref="ITenantOwned"/> CLR type known to the model today. Every entry
    /// here is intentionally keyed by <c>typeof(...)</c> rather than a string, so a rename is a
    /// compile error, not a silently-orphaned dictionary key.
    /// </summary>
    private static readonly IReadOnlyDictionary<Type, Func<Guid, EntityFixture>> EntityFixtureFactories =
        new Dictionary<Type, Func<Guid, EntityFixture>>
        {
            [typeof(User)] = tenantId => new EntityFixture(
                [new User(tenantId, $"user-{Guid.NewGuid():N}@test.dev", UserRole.PM, "hash")]),

            [typeof(Project)] = tenantId => new EntityFixture(
                [Project.Create(
                    tenantId, "Project", $"CODE-{Guid.NewGuid():N}", "Owner",
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(6), 100m, DateTimeOffset.UtcNow)]),

            [typeof(WBSNode)] = tenantId => new EntityFixture(
                [new WBSNode(tenantId, Guid.NewGuid(), "C1", "Title", 10m)]),

            [typeof(Activity)] = tenantId => new EntityFixture(
                [new Activity(
                    tenantId, Guid.NewGuid(), "A-1", "Name",
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(10), 10, 1_000m)]),

            [typeof(ActivityRelation)] = tenantId => new EntityFixture(
                [new ActivityRelation(tenantId, Guid.NewGuid(), Guid.NewGuid(), RelationType.FS)]),

            [typeof(Calendar)] = tenantId => new EntityFixture(
                [new Calendar(tenantId, Guid.NewGuid(), "Cal", WorkingDays.Weekdays)]),

            [typeof(CalendarException)] = tenantId =>
            {
                var calendar = new Calendar(tenantId, Guid.NewGuid(), "Cal", WorkingDays.Weekdays);
                var exception = calendar.AddException(DateTimeOffset.UtcNow, isWorkingDay: false, "Holiday");
                return new EntityFixture([calendar, exception]);
            },

            [typeof(ActivityProgressLog)] = tenantId =>
            {
                var activity = new Activity(
                    tenantId, Guid.NewGuid(), "A-1", "Name",
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(10), 10, 1_000m);
                var entry = activity.RecordProgress(
                    DateTimeOffset.UtcNow, 50m, null, Guid.NewGuid(), ProgressSource.Manual, DateTimeOffset.UtcNow);
                return new EntityFixture([activity, entry]);
            },

            [typeof(AuditLog)] = tenantId => new EntityFixture(
                [new AuditLog(
                    tenantId, "Project", Guid.NewGuid(), AuditAction.Created,
                    userId: Guid.NewGuid(), beforeJson: null, afterJson: "{}", timestamp: DateTimeOffset.UtcNow)]),

            // ApprovalPolicy owns its ApprovalPolicyRule rows (backing-field navigation) - listing
            // both explicitly here mirrors the CalendarException fixture above; Add()-ing the
            // policy alone would cascade the rules in anyway, but being explicit keeps this fixture
            // self-documenting for the ApprovalPolicyRule theory case specifically.
            [typeof(ApprovalPolicy)] = tenantId =>
            {
                var policy = ApprovalPolicy.CreateInitialVersion(
                    tenantId, projectId: null, ApprovalDocumentType.VariationOrder, DateTimeOffset.UtcNow,
                    [new ApprovalPolicyRuleInput(StepNo: 1, MinAmount: 0m, MaxAmount: null, RequiredRole: UserRole.PM)]);
                return new EntityFixture([policy, .. policy.Rules]);
            },

            [typeof(ApprovalPolicyRule)] = tenantId =>
            {
                var policy = ApprovalPolicy.CreateInitialVersion(
                    tenantId, projectId: null, ApprovalDocumentType.PaymentCertificate, DateTimeOffset.UtcNow,
                    [new ApprovalPolicyRuleInput(StepNo: 1, MinAmount: 0m, MaxAmount: null, RequiredRole: UserRole.QS)]);
                return new EntityFixture([policy, .. policy.Rules]);
            },

            [typeof(ApprovalAction)] = tenantId => new EntityFixture(
                [new ApprovalAction(
                    tenantId, ApprovalDocumentType.VariationOrder, Guid.NewGuid(), revisionNo: 1, stepNo: 1,
                    actorUserId: Guid.NewGuid(), actorRoleAtTime: UserRole.PM, action: ApprovalActionType.Submit,
                    comment: null, actedAt: DateTimeOffset.UtcNow, approvalPolicyId: Guid.NewGuid(), approvalPolicyVersion: 1)]),

            [typeof(FileImportJob)] = tenantId => new EntityFixture(
                [new FileImportJob(
                    tenantId, Guid.NewGuid(), "schedule.xer", ImportFileFormat.Xer,
                    createdByUserId: Guid.NewGuid(), startedAt: DateTimeOffset.UtcNow)]),

            [typeof(EvmPeriodSnapshot)] = tenantId => new EntityFixture(
                [new EvmPeriodSnapshot(
                    tenantId, Guid.NewGuid(), DateTimeOffset.UtcNow,
                    bac: 1_000_000.00m, pv: 400_000.00m, ev: 300_000.00m, ac: 350_000.00m,
                    eacVariant: EacVariant.CpiBased, performanceFactor: 1.166667m, eac: 1_166_666.67m,
                    etc: 816_666.67m, vac: -166_666.67m, createdAt: DateTimeOffset.UtcNow, createdByUserId: Guid.NewGuid())]),

            // actual-cost.md §9 (ADR-0013) - registered here per this file's own S1-QA-01 DoD
            // comment: a new ITenantOwned entity must never silently fall outside isolation
            // coverage.
            [typeof(ActualCostEntry)] = tenantId => new EntityFixture(
                [new ActualCostEntry(
                    tenantId, Guid.NewGuid(), wbsNodeId: null, activityId: null,
                    CostCategory.Material, ActualCostEntryType.Actual, ActualCostSource.ManualEntry,
                    amount: 1_000.00m, incurredDate: DateTimeOffset.UtcNow, postedAt: DateTimeOffset.UtcNow,
                    postedByUserId: Guid.NewGuid(), reversesEntryId: null, documentReference: null,
                    costCode: null, vendorName: null, note: null, fileImportJobId: null, paidDate: null,
                    quantity: null, unitOfMeasure: null)]),

            // payment-retention.md / S9-BE-01 - registered here per this file's own S1-QA-01 DoD
            // comment: a new ITenantOwned entity must never silently fall outside isolation coverage.
            [typeof(PaymentCertificate)] = tenantId => new EntityFixture(
                [new PaymentCertificate(
                    tenantId, Guid.NewGuid(), milestoneNo: 1, "Period 1", milestoneValue: 1_000_000.00m,
                    previousCumulativeApprovePct: 0m, createdByUserId: Guid.NewGuid())]),

            // Security review sprint-09.md H-01 fix - registered here per this file's own S1-QA-01
            // DoD comment: a new ITenantOwned entity must never silently fall outside isolation
            // coverage. PaymentCertificate owns its ApprovalSteps rows (backing-field navigation) -
            // listing both explicitly here mirrors the ApprovalPolicy/ApprovalPolicyRule fixture
            // above; Add()-ing the certificate alone would cascade the steps in anyway, but being
            // explicit keeps this fixture self-documenting for the PaymentCertificateApprovalStep
            // theory case specifically.
            [typeof(PaymentCertificateApprovalStep)] = tenantId =>
            {
                var certificate = new PaymentCertificate(
                    tenantId, Guid.NewGuid(), milestoneNo: 1, "Period 1", milestoneValue: 1_000_000.00m,
                    previousCumulativeApprovePct: 0m, createdByUserId: Guid.NewGuid());
                certificate.SetPeriodClaim(100m, null, null, 1_000_000.00m, 0m, 0m, 1_000_000.00m);
                certificate.Submit(
                    [new PaymentCertificateApprovalStepInput(1, UserRole.QS, 1)],
                    Guid.NewGuid(), 1, false, Guid.NewGuid(), DateTimeOffset.UtcNow);
                return new EntityFixture([certificate, .. certificate.ApprovalSteps]);
            },

            // payment-retention.md §4 / S9-BE-04 - same reason as PaymentCertificate above.
            [typeof(ProjectFinanceLedger)] = tenantId => new EntityFixture(
                [ProjectFinanceLedger.CreateRetentionAccrual(
                    tenantId, Guid.NewGuid(), Guid.NewGuid(), 1_000.00m, DateTimeOffset.UtcNow)]),

            // domain-rules.md §2 / S10-BE-01 - registered here per this file's own S1-QA-01 DoD
            // comment: a new ITenantOwned entity must never silently fall outside isolation coverage.
            [typeof(VariationOrder)] = tenantId => new EntityFixture(
                [new VariationOrder(
                    tenantId, Guid.NewGuid(), $"VO-{Guid.NewGuid():N}", Guid.NewGuid(),
                    amount: 100_000.00m, description: "VO", justification: null, timeImpactDays: 0,
                    scopeItems: [new VariationOrderScopeItemInput(Guid.NewGuid(), 100_000.00m)])]),

            // VariationOrder owns its ScopeItems rows (backing-field navigation) - listing both
            // explicitly here mirrors the CalendarException/ApprovalPolicy fixtures above.
            [typeof(VariationOrderScopeItem)] = tenantId =>
            {
                var variationOrder = new VariationOrder(
                    tenantId, Guid.NewGuid(), $"VO-{Guid.NewGuid():N}", Guid.NewGuid(),
                    amount: 100_000.00m, description: "VO", justification: null, timeImpactDays: 0,
                    scopeItems: [new VariationOrderScopeItemInput(Guid.NewGuid(), 100_000.00m)]);
                return new EntityFixture([variationOrder, .. variationOrder.ScopeItems]);
            },

            // Security review sprint-09.md H-01 fix, reused for the VO (domain-rules.md §2.1) -
            // registered here for the same reason PaymentCertificateApprovalStep is above.
            // VariationOrder owns its ApprovalSteps rows (backing-field navigation).
            [typeof(VariationOrderApprovalStep)] = tenantId =>
            {
                var variationOrder = new VariationOrder(
                    tenantId, Guid.NewGuid(), $"VO-{Guid.NewGuid():N}", Guid.NewGuid(),
                    amount: 100_000.00m, description: "VO", justification: null, timeImpactDays: 0,
                    scopeItems: [new VariationOrderScopeItemInput(Guid.NewGuid(), 100_000.00m)]);
                variationOrder.Submit(
                    [new VariationOrderApprovalStepInput(1, UserRole.PM, 1)],
                    Guid.NewGuid(), 1, false, Guid.NewGuid(), DateTimeOffset.UtcNow);
                return new EntityFixture([variationOrder, .. variationOrder.ApprovalSteps]);
            },

            // S11-BE-01 (US-11.1) - registered here per this file's own S1-QA-01 DoD comment: a new
            // ITenantOwned entity must never silently fall outside isolation test coverage.
            [typeof(DailyWeatherLog)] = tenantId => new EntityFixture(
                [DailyWeatherLog.CreateOriginal(
                    tenantId, Guid.NewGuid(), DateTimeOffset.UtcNow, WeatherCondition.HeavyRain, "ฝนตกหนัก", 42.5m,
                    WeatherImpact.FullStoppage, "หยุดเทคอนกรีตโซน B ครึ่งวัน", 8.00m, Guid.NewGuid(),
                    DateTimeOffset.UtcNow, [])]),

            // DailyWeatherLog owns its AffectedActivities rows (backing-field navigation) - listing
            // both explicitly here mirrors the CalendarException/VariationOrderScopeItem fixtures
            // above. The referenced ActivityId is a bare Guid with no real Activity row behind it -
            // same established pattern VariationOrderScopeItem's own fixture already uses (the EF
            // Core InMemory provider used by TestDbContextFactory does not enforce FK constraints).
            [typeof(DailyWeatherLogActivity)] = tenantId =>
            {
                var log = DailyWeatherLog.CreateOriginal(
                    tenantId, Guid.NewGuid(), DateTimeOffset.UtcNow, WeatherCondition.HeavyRain, "ฝนตกหนัก", 42.5m,
                    WeatherImpact.FullStoppage, "หยุดเทคอนกรีตโซน B ครึ่งวัน", 8.00m, Guid.NewGuid(),
                    DateTimeOffset.UtcNow, [Guid.NewGuid()]);
                return new EntityFixture([log, .. log.AffectedActivities]);
            },

            // S11-BE-03 (US-11.2) - same reason as DailyWeatherLog above.
            [typeof(IssueLog)] = tenantId => new EntityFixture(
                [new IssueLog(
                    tenantId, Guid.NewGuid(), "เหล็กเส้น DB25 ส่งช้า", "ซัพพลายเออร์แจ้งเลื่อน 5 วัน", "จัดซื้อ",
                    DateTimeOffset.UtcNow, Guid.NewGuid(), DateTimeOffset.UtcNow)]),

            // ADR-0019 (domain-rules.md weather-eot §4.3) - registered here per this file's own
            // S1-QA-01 DoD comment: a new ITenantOwned entity must never silently fall outside
            // isolation test coverage. No children for the CpmRun theory case itself - mirrors
            // DailyWeatherLog's own fixture above (an empty child collection is enough to prove the
            // parent row's own tenant isolation; the child theory cases below cover the children).
            [typeof(CpmRun)] = tenantId => new EntityFixture(
                [CpmRun.Capture(
                    tenantId, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, projectDurationDays: 10,
                    Guid.NewGuid(), CpmRunTrigger.Manual, [], [])]),

            // CpmRun owns its Activities/Relations rows (backing-field navigations) - listing the
            // run and its children explicitly here mirrors the DailyWeatherLog/DailyWeatherLogActivity
            // fixtures above.
            [typeof(CpmRunActivity)] = tenantId =>
            {
                var activityId = Guid.NewGuid();
                var run = CpmRun.Capture(
                    tenantId, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, projectDurationDays: 10,
                    Guid.NewGuid(), CpmRunTrigger.Manual,
                    [new CpmRunActivityInput(activityId, 5, 0, 5, 0, 5, TotalFloat: 0, FreeFloat: 0, IsCritical: true)], []);
                return new EntityFixture([run, .. run.Activities]);
            },

            [typeof(CpmRunRelation)] = tenantId =>
            {
                var predecessorId = Guid.NewGuid();
                var successorId = Guid.NewGuid();
                var run = CpmRun.Capture(
                    tenantId, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, projectDurationDays: 10,
                    Guid.NewGuid(), CpmRunTrigger.Manual,
                    [
                        new CpmRunActivityInput(predecessorId, 5, 0, 5, 0, 5, 0, 0, true),
                        new CpmRunActivityInput(successorId, 3, 5, 8, 5, 8, 0, 0, true),
                    ],
                    [new CpmRunRelationInput(predecessorId, successorId, RelationType.FS, LagDays: 0)]);
                return new EntityFixture([run, .. run.Relations]);
            },

            // S11-BE-02 (domain-rules.md weather-eot) - registered here per this file's own
            // S1-QA-01 DoD comment: a new ITenantOwned entity must never silently fall outside
            // isolation test coverage.
            [typeof(ProjectEotPolicy)] = tenantId => new EntityFixture(
                [new ProjectEotPolicy(tenantId, Guid.NewGuid())]),

            // No children for the EotEvaluation theory case itself - mirrors CpmRun's own fixture
            // above (an empty child collection is enough to prove the parent row's own tenant
            // isolation; the child theory cases below cover the children).
            [typeof(EotEvaluation)] = tenantId => new EntityFixture(
                [EotEvaluation.Capture(
                    tenantId, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                    Guid.NewGuid(), EotCriticalityBasis.Contemporaneous, EotConfidence.Substantiated,
                    asScheduledDurationDays: 10, impactedDurationDays: 10, eotEligibleDays: 0,
                    countableStoppageDayCount: 0, serialChainAbsorbedDayCount: 0, distinctCountableDateCount: 0,
                    unattributedStoppageDayCount: 0,
                    policySnapshotJson: "{}", latestNoticeDate: null, noticeWindowExpired: null, runs: [], sources: [],
                    drivers: [])]),

            // EotEvaluation owns its Runs/Sources/Drivers rows (backing-field navigations) - listing
            // the evaluation and each child explicitly here mirrors CpmRun/CpmRunActivity's identical
            // pattern.
            [typeof(EotEvaluationRun)] = tenantId =>
            {
                var evaluation = EotEvaluation.Capture(
                    tenantId, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                    Guid.NewGuid(), EotCriticalityBasis.Contemporaneous, EotConfidence.Substantiated,
                    asScheduledDurationDays: 10, impactedDurationDays: 11, eotEligibleDays: 1,
                    countableStoppageDayCount: 1, serialChainAbsorbedDayCount: 0, distinctCountableDateCount: 1,
                    unattributedStoppageDayCount: 0,
                    policySnapshotJson: "{}", latestNoticeDate: null, noticeWindowExpired: null,
                    runs: [new EotEvaluationRunInput(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 10, 11, 1)],
                    sources: [], drivers: []);
                return new EntityFixture([evaluation, .. evaluation.Runs]);
            },

            [typeof(EotEvaluationSource)] = tenantId =>
            {
                var evaluation = EotEvaluation.Capture(
                    tenantId, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                    Guid.NewGuid(), EotCriticalityBasis.Contemporaneous, EotConfidence.Substantiated,
                    asScheduledDurationDays: 10, impactedDurationDays: 10, eotEligibleDays: 0,
                    countableStoppageDayCount: 0, serialChainAbsorbedDayCount: 0, distinctCountableDateCount: 0,
                    unattributedStoppageDayCount: 0,
                    policySnapshotJson: "{}", latestNoticeDate: null, noticeWindowExpired: null, runs: [],
                    sources: [new EotEvaluationSourceInput(Guid.NewGuid(), 1.00m, null)], drivers: []);
                return new EntityFixture([evaluation, .. evaluation.Sources]);
            },

            [typeof(EotEvaluationDriver)] = tenantId =>
            {
                var evaluation = EotEvaluation.Capture(
                    tenantId, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                    Guid.NewGuid(), EotCriticalityBasis.Contemporaneous, EotConfidence.Substantiated,
                    asScheduledDurationDays: 10, impactedDurationDays: 11, eotEligibleDays: 1,
                    countableStoppageDayCount: 1, serialChainAbsorbedDayCount: 0, distinctCountableDateCount: 1,
                    unattributedStoppageDayCount: 0,
                    policySnapshotJson: "{}", latestNoticeDate: null, noticeWindowExpired: null, runs: [], sources: [],
                    drivers: [new EotEvaluationDriverInput(
                        Guid.NewGuid(), Guid.NewGuid(), "C", "Activity C", StoppageDays: 1, TotalFloatAtRun: 0,
                        WasCriticalAtRun: true, IsOnImpactedCriticalPath: true, IndicativeEotDays: 1, MarginalEotDays: 1,
                        RemainingFloatAfter: 0, UnclaimedFractionalHours: null)]);
                return new EntityFixture([evaluation, .. evaluation.Drivers]);
            },

            // S12-BE-01 (US-12.1) - registered here per this file's own S1-QA-01 DoD comment: a new
            // ITenantOwned entity must never silently fall outside isolation test coverage.
            [typeof(Photo)] = tenantId => new EntityFixture(
                [new Photo(
                    tenantId, Guid.NewGuid(), null, "โซน B", PhotoImageFormat.Jpeg, 1_024,
                    Guid.NewGuid(), DateTimeOffset.UtcNow, null)]),

            // S12-BE-02 (US-12.2, domain-rules.md manpower-equipment §4.3) - registered here per
            // this file's own S1-QA-01 DoD comment: a new ITenantOwned entity must never silently
            // fall outside isolation test coverage.
            [typeof(WorkCategory)] = tenantId => new EntityFixture(
                [new WorkCategory(tenantId, null, $"C{Guid.NewGuid():N}"[..8], "งานโครงสร้าง", "Structure", 1)]),

            // §4.7 - the append-only site log.
            [typeof(ManpowerEquipmentLog)] = tenantId => new EntityFixture(
                [ManpowerEquipmentLog.CreateOriginal(
                    tenantId, Guid.NewGuid(), DateTimeOffset.UtcNow, Shift.Day, Guid.NewGuid(), Guid.NewGuid(), null,
                    LabourType.OwnDirect, null, 25, 200.00m, 0m, false, 0, 0m, 0m, "งานโครงสร้าง", null,
                    Guid.NewGuid(), DateTimeOffset.UtcNow, allowDuplicateOverride: false)]),

            // §4.6(a) - the editable, audited manning plan.
            [typeof(ManpowerPlan)] = tenantId => new EntityFixture(
                [new ManpowerPlan(
                    tenantId, Guid.NewGuid(), null, Guid.NewGuid(), DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow.AddDays(7), plannedWorkerCount: 20, plannedManHours: 160.00m)]),

            // S13-BE-01/S13-DB-01 (ADR-0005 US-13.1) - registered here per this file's own S1-QA-01
            // DoD comment: a new ITenantOwned entity must never silently fall outside isolation test
            // coverage. Cross-tenant independence for this specific entity is additionally exercised
            // end-to-end in EfIdempotencyStoreTests/IdempotencyMiddlewareTests (the same key string
            // used by two different tenants never collides or leaks).
            [typeof(IdempotencyKey)] = tenantId => new EntityFixture(
                [new IdempotencyKey(
                    tenantId, $"key-{Guid.NewGuid()}", "POST", "/api/v1/projects/x/photos",
                    new string('a', 64), Guid.NewGuid(), DateTimeOffset.UtcNow)]),

            // S14-BE-01 - registered here per this file's own S1-QA-01 DoD comment: a new
            // ITenantOwned entity must never silently fall outside isolation test coverage. No
            // children for the Baseline theory case itself - mirrors CpmRun/EotEvaluation's own
            // fixtures above (an empty snapshot collection is enough to prove the parent row's own
            // tenant isolation; the child theory case below covers BaselineActivitySnapshot).
            [typeof(Baseline)] = tenantId => new EntityFixture(
                [Domain.Entities.Baseline.Capture(
                    tenantId, Guid.NewGuid(), "Baseline", DateTimeOffset.UtcNow, Guid.NewGuid(), 1_000_000.00m, [])]),

            // Baseline owns its Snapshots rows (backing-field navigation) - listing the baseline and
            // its child explicitly here mirrors CpmRun/CpmRunActivity's identical pattern.
            [typeof(BaselineActivitySnapshot)] = tenantId =>
            {
                var baseline = Domain.Entities.Baseline.Capture(
                    tenantId, Guid.NewGuid(), "Baseline", DateTimeOffset.UtcNow, Guid.NewGuid(), 1_000_000.00m,
                    [new BaselineActivitySnapshotInput(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(10), 10, 1_000.00m)]);
                return new EntityFixture([baseline, .. baseline.Snapshots]);
            },
        };

    /// <summary>
    /// Discovers every <see cref="ITenantOwned"/> entity in the built EF Core model by
    /// reflection instead of a hand-maintained list of type names, so a newly-added
    /// tenant-owned entity is picked up as a new theory case automatically (S1-QA-01 DoD).
    /// </summary>
    public static IEnumerable<object[]> TenantOwnedEntityTypes()
    {
        var options = new DbContextOptionsBuilder<CmPlusDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new CmPlusDbContext(options, new FakeTenantProvider(Guid.NewGuid()));

        return context.Model.GetEntityTypes()
            .Select(t => t.ClrType)
            .Where(t => typeof(ITenantOwned).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => new object[] { t })
            .ToList();
    }

    private static EntityFixture CreateFixture(Type entityType, Guid tenantId)
    {
        if (!EntityFixtureFactories.TryGetValue(entityType, out var factory))
        {
            throw new InvalidOperationException(
                $"No tenant-isolation test fixture registered for '{entityType.Name}'. Every " +
                $"ITenantOwned entity must have one in {nameof(TenantIsolationTests)}." +
                $"{nameof(EntityFixtureFactories)} - a new tenant-owned table must never " +
                "silently fall outside isolation test coverage (S1-QA-01 DoD).");
        }

        return factory(tenantId);
    }

    /// <summary>
    /// Reflection helper: <see cref="DbContext"/> only exposes the generic
    /// <c>Set&lt;TEntity&gt;()</c> on this EF Core version (verified - there is no non-generic
    /// <c>Set(Type)</c> overload), so a runtime <see cref="Type"/> needs
    /// <see cref="MethodInfo.MakeGenericMethod"/> to obtain the matching <c>DbSet</c>. The
    /// returned <c>DbSet&lt;TEntity&gt;</c> always implements the non-generic
    /// <see cref="System.Collections.IEnumerable"/> regardless of <paramref name="entityType"/>,
    /// so enumerating it (which executes the query) and casting each row to
    /// <see cref="ITenantOwned"/> works uniformly for every entity type.
    /// </summary>
    private static List<ITenantOwned> QueryAllOfType(CmPlusDbContext context, Type entityType, bool ignoreQueryFilters = false)
    {
        var setMethod = typeof(DbContext)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => m.Name == nameof(DbContext.Set) && m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
            .MakeGenericMethod(entityType);

        object queryable = setMethod.Invoke(context, null)!;

        if (ignoreQueryFilters)
        {
            var ignoreMethod = typeof(EntityFrameworkQueryableExtensions)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == nameof(EntityFrameworkQueryableExtensions.IgnoreQueryFilters) && m.GetParameters().Length == 1)
                .MakeGenericMethod(entityType);

            queryable = ignoreMethod.Invoke(null, [queryable])!;
        }

        return ((System.Collections.IEnumerable)queryable).Cast<ITenantOwned>().ToList();
    }

    [Theory]
    [MemberData(nameof(TenantOwnedEntityTypes))]
    public async Task Global_Query_Filter_Hides_Other_Tenants_Rows_For_Every_Tenant_Owned_Entity(Type entityType)
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantA);

        using (var seedContext = factory.CreateContext())
        {
            foreach (var entity in CreateFixture(entityType, tenantA).AllEntitiesToPersist)
            {
                seedContext.Add((object)entity);
            }

            await seedContext.SaveChangesAsync();
        }

        factory.TenantProvider.TenantId = tenantB;
        using (var seedContext = factory.CreateContext())
        {
            foreach (var entity in CreateFixture(entityType, tenantB).AllEntitiesToPersist)
            {
                seedContext.Add((object)entity);
            }

            await seedContext.SaveChangesAsync();
        }

        // The actual S1-QA-01 DoD assertion, parameterized over every ITenantOwned entity:
        // querying as tenant A must return zero of tenant B's rows for this entity type.
        factory.TenantProvider.TenantId = tenantA;
        using (var readContext = factory.CreateContext())
        {
            var rows = QueryAllOfType(readContext, entityType);

            // Sanity check: without this, a filter bug that hides EVERYTHING (not just tenant
            // B) would make "DoesNotContain tenantB" vacuously true on an empty result set.
            Assert.NotEmpty(rows);
            Assert.DoesNotContain(rows, r => r.TenantId == tenantB);
            Assert.All(rows, r => Assert.Equal(tenantA, r.TenantId));
        }

        // Bypassing the filter proves tenant B's row genuinely exists in the database - the
        // filter is hiding it, not the data being absent (generalizes the escape-hatch proof in
        // Global_Query_Filter_Hides_Other_Tenants_Rows above to every entity type).
        using (var adminContext = factory.CreateContext())
        {
            var allRows = QueryAllOfType(adminContext, entityType, ignoreQueryFilters: true);

            Assert.Contains(allRows, r => r.TenantId == tenantA);
            Assert.Contains(allRows, r => r.TenantId == tenantB);
        }
    }

    [Theory]
    [MemberData(nameof(TenantOwnedEntityTypes))]
    public async Task SaveChanges_Overwrites_A_Payload_Supplied_TenantId_For_Every_Tenant_Owned_Entity(Type entityType)
    {
        var correctTenantId = Guid.NewGuid();
        var attackerSuppliedTenantId = Guid.NewGuid(); // e.g. forged in a request payload

        var factory = new TestDbContextFactory(correctTenantId);

        using (var context = factory.CreateContext())
        {
            // Built "as" the attacker-supplied tenant - stands in for a value that leaked in
            // from a client payload/DTO rather than ITenantProvider (S1-BE-03 DoD).
            foreach (var entity in CreateFixture(entityType, attackerSuppliedTenantId).AllEntitiesToPersist)
            {
                context.Add((object)entity);
            }

            await context.SaveChangesAsync();
        }

        using (var verifyContext = factory.CreateContext())
        {
            var stored = QueryAllOfType(verifyContext, entityType, ignoreQueryFilters: true);

            Assert.NotEmpty(stored);
            Assert.All(stored, r => Assert.Equal(correctTenantId, r.TenantId));
            Assert.DoesNotContain(stored, r => r.TenantId == attackerSuppliedTenantId);
        }
    }
}
