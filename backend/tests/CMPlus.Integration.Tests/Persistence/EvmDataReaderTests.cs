using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Persistence;

/// <summary>
/// S7-BE-01/03: <see cref="EvmDataReader"/> against a real <see cref="CmPlusDbContext"/> - proves the
/// project-wide reader reuses the identical ADR-0009 step-function rule
/// <see cref="ActivityProgressReaderTests"/> already proves for the single-activity reader (same
/// "latest PeriodEndDate &lt;= asOf, ties broken by RecordedAt, 0 if none" behaviour, just batched
/// project-wide), scoped to the requested project (never another project's activities in the same
/// tenant, mirroring <see cref="GanttActivityReaderTests"/>'s own isolation proof).
/// </summary>
public class EvmDataReaderTests
{
    private static Project CreateProject(Guid tenantId, string code, EacVariant eacVariant = EacVariant.CpiBased) =>
        Project.Create(
            tenantId, $"Project {code}", code, "Owner",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(12), bac: 1_000_000m, dataDate: DateTimeOffset.UtcNow);

    [Fact]
    public async Task GetProjectSettingsAsync_Returns_Null_When_The_Project_Does_Not_Exist()
    {
        var factory = new TestDbContextFactory(Guid.NewGuid());
        using var context = factory.CreateContext();
        var reader = new EvmDataReader(context);

        var settings = await reader.GetProjectSettingsAsync(Guid.NewGuid());

        Assert.Null(settings);
    }

    [Fact]
    public async Task GetProjectSettingsAsync_Returns_The_Projects_DataDate_And_Eac_Fields()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var expectedDataDate = DateTimeOffset.Parse("2026-06-30T00:00:00+07:00");

        var project = Project.Create(
            tenantId, "Project P", "P", "Owner",
            DateTimeOffset.Parse("2026-01-01T00:00:00+07:00"), DateTimeOffset.Parse("2027-01-01T00:00:00+07:00"),
            bac: 2_500_000m, dataDate: expectedDataDate);
        project.SetEacVariantDefault(EacVariant.CpiSpiBased);
        project.SetEacCustomPerformanceFactor(1.20m);
        project.SetEacManualEtc(900_000m);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(project);
            await seedContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new EvmDataReader(readContext);

        var settings = await reader.GetProjectSettingsAsync(project.Id);

        Assert.NotNull(settings);
        // domain-rules.md §5.5(c): BAC is deliberately NOT part of this projection any more - it is a
        // function of the data date, resolved separately via GetBacAsOfAsync below, never a scalar
        // read here (see ProjectEvmSettings' own remarks).
        Assert.Equal(expectedDataDate, settings!.ProjectDataDate);
        Assert.Equal(EacVariant.CpiSpiBased, settings.EacVariantDefault);
        Assert.Equal(1.20m, settings.EacCustomPerformanceFactor);
        Assert.Equal(900_000m, settings.EacManualEtc);
        Assert.Null(settings.EacManualEtcStaleSince); // never gone stale - no VO has been approved.
    }

    /// <summary>domain-rules.md §5.7: the half of the gap S10-QA found - the stamp itself
    /// (<c>Project.EacManualEtcStaleSince</c>) already worked before this fix; this proves it is
    /// actually read back by the reader that feeds <c>EacCalculator</c>, not just written.</summary>
    [Fact]
    public async Task GetProjectSettingsAsync_Returns_EacManualEtcStaleSince_When_A_Vo_Has_Made_The_Manual_Etc_Stale()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var approvedAt = DateTimeOffset.Parse("2026-08-10T09:00:00+07:00");

        var project = Project.Create(
            tenantId, "Project P", "P", "Owner",
            DateTimeOffset.Parse("2026-01-01T00:00:00+07:00"), DateTimeOffset.Parse("2027-01-01T00:00:00+07:00"),
            bac: 2_500_000m, dataDate: DateTimeOffset.Parse("2026-06-30T00:00:00+07:00"));
        project.SetEacManualEtc(900_000m);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(project);
            await seedContext.SaveChangesAsync();
        }

        using (var writeContext = factory.CreateContext())
        {
            var tracked = await writeContext.Projects.SingleAsync(p => p.Id == project.Id);
            tracked.ApplyVariationOrderApproval(1_000_000.00m, approvedAt);
            await writeContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new EvmDataReader(readContext);

        var settings = await reader.GetProjectSettingsAsync(project.Id);

        Assert.Equal(approvedAt, settings!.EacManualEtcStaleSince);
    }

    // -----------------------------------------------------------------------------------------
    // GetBacAsOfAsync (domain-rules.md §5.5(c), S10-QA gap fix): BAC(t) = OriginalBac + sum of
    // Approved VariationOrder.Amount with ApprovedAt <= asOf - never a live Project.BAC read.
    // -----------------------------------------------------------------------------------------

    private static VariationOrder CreateApprovedVo(
        Guid tenantId, Guid projectId, Guid activityId, decimal amount, DateTimeOffset approvedAt, string voNumber)
    {
        var vo = new VariationOrder(
            tenantId, projectId, voNumber, Guid.NewGuid(), amount, "Test VO", null, 0,
            [new VariationOrderScopeItemInput(activityId, amount)]);
        vo.Submit(
            [new VariationOrderApprovalStepInput(1, UserRole.PM, 1)],
            approvalPolicyId: Guid.NewGuid(), approvalPolicyVersion: 1,
            allowSelfApproval: false, submittedByUserId: Guid.NewGuid(), submittedAt: approvedAt.AddDays(-1));
        vo.Approve(Guid.NewGuid(), UserRole.PM, UserRole.PM, allowSelfApproval: false, approvedAt);
        return vo;
    }

    [Fact]
    public async Task GetBacAsOfAsync_Returns_Zero_For_An_Unknown_Project()
    {
        var factory = new TestDbContextFactory(Guid.NewGuid());
        using var context = factory.CreateContext();
        var reader = new EvmDataReader(context);

        var bac = await reader.GetBacAsOfAsync(Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(0m, bac);
    }

    [Fact]
    public async Task GetBacAsOfAsync_Returns_The_Original_Bac_When_No_Variation_Order_Has_Ever_Been_Approved()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var project = CreateProject(tenantId, "P");

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(project);
            await seedContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new EvmDataReader(readContext);

        var bac = await reader.GetBacAsOfAsync(project.Id, DateTimeOffset.UtcNow.AddYears(1));

        Assert.Equal(1_000_000m, bac); // CreateProject's own bac literal.
    }

    /// <summary>
    /// The exact defect this fixture pins: a historical query at a data date BEFORE the VO's own
    /// <c>ApprovedAt</c> must keep reading the pre-VO figure even after that VO is later approved -
    /// never silently rewritten the instant approval happens (domain-rules.md §5.5(c)).
    /// </summary>
    [Fact]
    public async Task GetBacAsOfAsync_Excludes_A_Variation_Order_Approved_After_The_Requested_Date()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var project = CreateProject(tenantId, "P");
        var node = new WBSNode(tenantId, project.Id, "1", "Structure", 100m);
        var activity = new Activity(
            tenantId, node.Id, "A-1", "Foundation",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-06-01T00:00:00Z"), 150, 0m);
        var approvedAt = DateTimeOffset.Parse("2026-07-15T00:00:00Z");
        var pastDate = DateTimeOffset.Parse("2026-03-01T00:00:00Z"); // well before approvedAt.

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(project);
            seedContext.WBSNodes.Add(node);
            seedContext.Activities.Add(activity);
            seedContext.VariationOrders.Add(CreateApprovedVo(tenantId, project.Id, activity.Id, 10_000_000.00m, approvedAt, "VO-1"));
            await seedContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new EvmDataReader(readContext);

        var bacBeforeApproval = await reader.GetBacAsOfAsync(project.Id, pastDate);
        var bacAtApproval = await reader.GetBacAsOfAsync(project.Id, approvedAt);
        var bacAfterApproval = await reader.GetBacAsOfAsync(project.Id, approvedAt.AddDays(1));

        Assert.Equal(1_000_000m, bacBeforeApproval); // unaffected - the VO's own ApprovedAt is later.
        Assert.Equal(11_000_000m, bacAtApproval); // ApprovedAt <= asOf is inclusive (domain-rules.md §5.5(c)).
        Assert.Equal(11_000_000m, bacAfterApproval);
    }

    [Fact]
    public async Task GetBacAsOfAsync_Excludes_A_Variation_Order_That_Is_Not_Approved()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var project = CreateProject(tenantId, "P");
        var node = new WBSNode(tenantId, project.Id, "1", "Structure", 100m);
        var activity = new Activity(
            tenantId, node.Id, "A-1", "Foundation",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-06-01T00:00:00Z"), 150, 0m);

        var pendingVo = new VariationOrder(
            tenantId, project.Id, "VO-PENDING", Guid.NewGuid(), 5_000_000.00m, "Test VO", null, 0,
            [new VariationOrderScopeItemInput(activity.Id, 5_000_000.00m)]);
        pendingVo.Submit(
            [new VariationOrderApprovalStepInput(1, UserRole.PM, 1)],
            approvalPolicyId: Guid.NewGuid(), approvalPolicyVersion: 1,
            allowSelfApproval: false, submittedByUserId: Guid.NewGuid(), submittedAt: DateTimeOffset.UtcNow);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(project);
            seedContext.WBSNodes.Add(node);
            seedContext.Activities.Add(activity);
            seedContext.VariationOrders.Add(pendingVo);
            await seedContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new EvmDataReader(readContext);

        var bac = await reader.GetBacAsOfAsync(project.Id, DateTimeOffset.UtcNow.AddYears(1));

        Assert.Equal(1_000_000m, bac); // still PendingApproval - contributes nothing.
    }

    [Fact]
    public async Task GetBacAsOfAsync_Sums_Multiple_Approved_Variation_Orders_Net_Signed()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var project = CreateProject(tenantId, "P");
        var node = new WBSNode(tenantId, project.Id, "1", "Structure", 100m);
        var activityAdd = new Activity(
            tenantId, node.Id, "A-1", "Add scope",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-06-01T00:00:00Z"), 150, 0m);
        var activityDeduct = new Activity(
            tenantId, node.Id, "A-2", "Deduct scope",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-06-01T00:00:00Z"), 150, 400_000.00m);
        var asOf = DateTimeOffset.Parse("2026-09-01T00:00:00Z");

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(project);
            seedContext.WBSNodes.Add(node);
            seedContext.Activities.AddRange(activityAdd, activityDeduct);
            seedContext.VariationOrders.Add(CreateApprovedVo(
                tenantId, project.Id, activityAdd.Id, 2_000_000.00m, DateTimeOffset.Parse("2026-07-01T00:00:00Z"), "VO-ADD"));
            seedContext.VariationOrders.Add(CreateApprovedVo(
                tenantId, project.Id, activityDeduct.Id, -300_000.00m, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), "VO-DEDUCT"));
            await seedContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new EvmDataReader(readContext);

        var bac = await reader.GetBacAsOfAsync(project.Id, asOf);

        // 1,000,000 (original) + 2,000,000 (Add) - 300,000 (Deduct) = 2,700,000.00 - net signed, not
        // gross absolute (R2's own routing-vs-effect distinction, domain-rules.md §5.1).
        Assert.Equal(2_700_000.00m, bac);
    }

    /// <summary>
    /// domain-rules.md §5.5(c) / sprint-10 security review H-02's required regression test:
    /// "Project.BAC == BAC(now)". <c>Project.BAC</c> is documented as "the maintained denormalisation
    /// of BAC(now)"; if that ever silently drifts (the exact H-02 failure mode - a nullable
    /// <c>OriginalBac</c> falling back to something other than a true reconstruction), a live read and
    /// a historical read taken at the current instant would disagree with no test catching it. Runs
    /// across several approved VOs (mixed Add/Deduct) specifically so the invariant is checked against
    /// a moved baseline, not merely a freshly-created project where the two would trivially agree.
    /// </summary>
    [Fact]
    public async Task Invariant_Project_BAC_Always_Equals_GetBacAsOfAsync_Evaluated_At_Now()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var project = CreateProject(tenantId, "P");
        var node = new WBSNode(tenantId, project.Id, "1", "Structure", 100m);
        var activityAdd = new Activity(
            tenantId, node.Id, "A-1", "Add scope",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-06-01T00:00:00Z"), 150, 0m);
        var activityDeduct = new Activity(
            tenantId, node.Id, "A-2", "Deduct scope",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-06-01T00:00:00Z"), 150, 900_000.00m);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(project);
            seedContext.WBSNodes.Add(node);
            seedContext.Activities.AddRange(activityAdd, activityDeduct);
            seedContext.VariationOrders.Add(CreateApprovedVo(
                tenantId, project.Id, activityAdd.Id, 3_000_000.00m, DateTimeOffset.Parse("2026-07-01T00:00:00Z"), "VO-ADD"));
            seedContext.VariationOrders.Add(CreateApprovedVo(
                tenantId, project.Id, activityDeduct.Id, -450_000.00m, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), "VO-DEDUCT"));
            await seedContext.SaveChangesAsync();
        }

        // Both VOs' approval effects (Project.ApplyVariationOrderApproval) only ever run through
        // ApproveVariationOrderCommandHandler in production; this test seeds pre-approved VariationOrder
        // rows directly (CreateApprovedVo), so it also applies the identical BAC delta to the live
        // Project row here - mirroring exactly what the handler's own atomic unit of work would have
        // done, without needing the full handler/repository stack for a reader-level invariant test.
        using (var applyContext = factory.CreateContext())
        {
            var tracked = await applyContext.Projects.SingleAsync(p => p.Id == project.Id);
            tracked.ApplyVariationOrderApproval(3_000_000.00m, DateTimeOffset.Parse("2026-07-01T00:00:00Z"));
            tracked.ApplyVariationOrderApproval(-450_000.00m, DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
            await applyContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new EvmDataReader(readContext);
        var now = DateTimeOffset.Parse("2026-09-01T00:00:00Z"); // after both VOs' ApprovedAt

        var liveBac = (await readContext.Projects.AsNoTracking().SingleAsync(p => p.Id == project.Id)).BAC;
        var bacAsOfNow = await reader.GetBacAsOfAsync(project.Id, now);

        Assert.Equal(3_550_000.00m, liveBac); // 1,000,000 (CreateProject) + 3,000,000 - 450,000
        Assert.Equal(liveBac, bacAsOfNow); // THE invariant: Project.BAC == BAC(now), always.
    }

    [Fact]
    public async Task GetActivityInputsAsync_Returns_The_StepFunction_Progress_For_Every_Activity_Including_One_With_No_Entry()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var project = CreateProject(tenantId, "P");
        var node = new WBSNode(tenantId, project.Id, "1", "Structure", 100m);

        var activityWithProgress = new Activity(
            tenantId, node.Id, "A-1", "Excavation",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-01-15T00:00:00Z"), 14, 1_000_000m);
        var activityWithoutProgress = new Activity(
            tenantId, node.Id, "A-2", "Piling",
            DateTimeOffset.Parse("2026-01-16T00:00:00Z"), DateTimeOffset.Parse("2026-01-30T00:00:00Z"), 14, 500_000m);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(project);
            seedContext.WBSNodes.Add(node);
            seedContext.Activities.AddRange(activityWithProgress, activityWithoutProgress);
            await seedContext.SaveChangesAsync();
        }

        using (var writeContext = factory.CreateContext())
        {
            var tracked = await writeContext.Activities.SingleAsync(a => a.Id == activityWithProgress.Id);
            var entry = tracked.RecordProgress(
                DateTimeOffset.Parse("2026-01-10T00:00:00Z"), 40m, null, Guid.NewGuid(),
                ProgressSource.Manual, DateTimeOffset.Parse("2026-01-11T00:00:00Z"));
            writeContext.ActivityProgressLogs.Add(entry);
            await writeContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new EvmDataReader(readContext);

        var inputs = await reader.GetActivityInputsAsync(project.Id, DateTimeOffset.Parse("2026-02-01T00:00:00Z"));

        Assert.Equal(2, inputs.Count);
        var withProgress = inputs.Single(i => i.ActivityId == activityWithProgress.Id);
        Assert.Equal(1_000_000m, withProgress.BudgetCost);
        Assert.Equal(40m, withProgress.ProgressPercentageAsOf);

        var withoutProgress = inputs.Single(i => i.ActivityId == activityWithoutProgress.Id);
        Assert.Equal(500_000m, withoutProgress.BudgetCost);
        Assert.Equal(0m, withoutProgress.ProgressPercentageAsOf); // no entry yet -> 0, not null/error.
    }

    [Fact]
    public async Task GetActivityInputsAsync_Reads_The_Latest_Entry_As_Of_The_Requested_Date_Not_The_Cache()
    {
        // ADR-0009: a historical read at t must reflect the step function at t, which can differ from
        // Activity.ProgressPercentage's own "latest overall" cache once a later period is recorded.
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var project = CreateProject(tenantId, "P");
        var node = new WBSNode(tenantId, project.Id, "1", "Structure", 100m);
        var activity = new Activity(
            tenantId, node.Id, "A-1", "Excavation",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-02-01T00:00:00Z"), 31, 1_000_000m);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(project);
            seedContext.WBSNodes.Add(node);
            seedContext.Activities.Add(activity);
            await seedContext.SaveChangesAsync();
        }

        using (var writeContext = factory.CreateContext())
        {
            var tracked = await writeContext.Activities.SingleAsync(a => a.Id == activity.Id);
            var week1 = tracked.RecordProgress(
                DateTimeOffset.Parse("2026-01-07T00:00:00Z"), 20m, null, Guid.NewGuid(),
                ProgressSource.Manual, DateTimeOffset.Parse("2026-01-08T00:00:00Z"));
            var week2 = tracked.RecordProgress(
                DateTimeOffset.Parse("2026-01-14T00:00:00Z"), 45m, null, Guid.NewGuid(),
                ProgressSource.Manual, DateTimeOffset.Parse("2026-01-15T00:00:00Z"));
            writeContext.ActivityProgressLogs.AddRange(week1, week2);
            await writeContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new EvmDataReader(readContext);

        // Historical read as of a date between the two entries -> the earlier (week 1) value, even
        // though Activity.ProgressPercentage's cache has already moved to 45.
        var historicalInputs = await reader.GetActivityInputsAsync(project.Id, DateTimeOffset.Parse("2026-01-10T00:00:00Z"));
        Assert.Equal(20m, Assert.Single(historicalInputs).ProgressPercentageAsOf);

        var currentInputs = await reader.GetActivityInputsAsync(project.Id, DateTimeOffset.Parse("2026-01-20T00:00:00Z"));
        Assert.Equal(45m, Assert.Single(currentInputs).ProgressPercentageAsOf);
    }

    [Fact]
    public async Task GetActivityInputsAsync_Excludes_Activities_Belonging_To_A_Different_Project_In_The_Same_Tenant()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var projectOne = CreateProject(tenantId, "P-ONE");
        var projectTwo = CreateProject(tenantId, "P-TWO");
        var nodeOne = new WBSNode(tenantId, projectOne.Id, "1", "Structure", 100m);
        var nodeTwo = new WBSNode(tenantId, projectTwo.Id, "1", "Structure", 100m);
        var activityOne = new Activity(
            tenantId, nodeOne.Id, "ONE-001", "Foundation",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-01-15T00:00:00Z"), 14, 1_000_000m);
        var activityTwo = new Activity(
            tenantId, nodeTwo.Id, "TWO-001", "Foundation",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-01-15T00:00:00Z"), 14, 1_000_000m);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.AddRange(projectOne, projectTwo);
            seedContext.WBSNodes.AddRange(nodeOne, nodeTwo);
            seedContext.Activities.AddRange(activityOne, activityTwo);
            await seedContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new EvmDataReader(readContext);

        var inputs = await reader.GetActivityInputsAsync(projectOne.Id, DateTimeOffset.UtcNow);

        var input = Assert.Single(inputs);
        Assert.Equal(activityOne.Id, input.ActivityId);
    }
}
