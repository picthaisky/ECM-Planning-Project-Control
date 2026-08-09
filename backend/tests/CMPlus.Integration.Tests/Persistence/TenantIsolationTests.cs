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
