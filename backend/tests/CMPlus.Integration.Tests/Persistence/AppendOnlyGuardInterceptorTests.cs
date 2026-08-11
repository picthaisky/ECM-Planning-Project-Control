using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Persistence;

/// <summary>
/// security review sprint-09.md M-01: "append-only" for <see cref="ApprovalAction"/> (and
/// <see cref="ProjectFinanceLedger"/>/<see cref="ActualCostEntry"/>/<see cref="EvmPeriodSnapshot"/>/
/// <see cref="AuditLog"/>) was a C#-API convention only - no mutator method/setter exists on the
/// entity, but nothing stopped an ordinary <see cref="CmPlusDbContext"/> from rewriting or deleting
/// the row directly via <c>context.Entry(...)</c>/<c>Remove(...)</c> (execution-verified, review
/// probe 7). <see cref="AppendOnlyGuardInterceptor"/> closes this structurally at
/// <c>SavingChanges</c>.
/// </summary>
public class AppendOnlyGuardInterceptorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-09T09:00:00+07:00");

    private static CmPlusDbContext CreateContext(string databaseName, FakeTenantProvider tenantProvider, bool withGuard)
    {
        var builder = new DbContextOptionsBuilder<CmPlusDbContext>().UseInMemoryDatabase(databaseName);
        if (withGuard)
        {
            builder.AddInterceptors(new AppendOnlyGuardInterceptor());
        }

        return new CmPlusDbContext(builder.Options, tenantProvider);
    }

    private static async Task<(Guid TenantId, Guid ActionId, string DatabaseName)> SeedOneApprovalActionAsync(bool withGuard)
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var databaseName = Guid.NewGuid().ToString();

        using var context = CreateContext(databaseName, tenantProvider, withGuard);
        var action = new ApprovalAction(
            tenantId, ApprovalDocumentType.PaymentCertificate, Guid.NewGuid(), revisionNo: 1, stepNo: 1,
            Guid.NewGuid(), UserRole.QS, ApprovalActionType.Approve, comment: "Original, correct comment.",
            Now, Guid.NewGuid(), 1);
        context.ApprovalActions.Add(action);
        await context.SaveChangesAsync();

        return (tenantId, action.Id, databaseName);
    }

    /// <summary>
    /// Reproduces review probe 7 exactly: proves the vulnerability exists on an <b>ordinary</b>
    /// <see cref="CmPlusDbContext"/> with no guard interceptor wired in - the baseline this fix
    /// closes. Kept as a permanent regression test (not just a one-off repro) so nobody can
    /// reintroduce a code path that constructs the DbContext without the guard and silently regress.
    /// </summary>
    [Fact]
    public async Task Without_The_Guard_An_Ordinary_DbContext_Allows_Rewriting_And_Deleting_An_ApprovalAction()
    {
        var (tenantId, actionId, databaseName) = await SeedOneApprovalActionAsync(withGuard: false);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using (var tamperContext = CreateContext(databaseName, tenantProvider, withGuard: false))
        {
            var action = await tamperContext.ApprovalActions.SingleAsync(a => a.Id == actionId);
            tamperContext.Entry(action).Property("Comment").CurrentValue = "TAMPERED";
            await tamperContext.SaveChangesAsync(); // succeeds today - the bug.
        }

        using (var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: false))
        {
            var tampered = await verifyContext.ApprovalActions.AsNoTracking().SingleAsync(a => a.Id == actionId);
            Assert.Equal("TAMPERED", tampered.Comment);
        }

        using (var deleteContext = CreateContext(databaseName, tenantProvider, withGuard: false))
        {
            var action = await deleteContext.ApprovalActions.SingleAsync(a => a.Id == actionId);
            deleteContext.ApprovalActions.Remove(action);
            await deleteContext.SaveChangesAsync(); // succeeds today - the bug.
        }

        using (var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: false))
        {
            Assert.Equal(0, await verifyContext.ApprovalActions.CountAsync());
        }
    }

    [Fact]
    public async Task With_The_Guard_Modifying_An_ApprovalAction_Throws_And_The_Original_Row_Survives()
    {
        var (tenantId, actionId, databaseName) = await SeedOneApprovalActionAsync(withGuard: true);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using (var tamperContext = CreateContext(databaseName, tenantProvider, withGuard: true))
        {
            var action = await tamperContext.ApprovalActions.SingleAsync(a => a.Id == actionId);
            tamperContext.Entry(action).Property("Comment").CurrentValue = "TAMPERED";

            await Assert.ThrowsAsync<InvalidOperationException>(() => tamperContext.SaveChangesAsync());
        }

        using var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: true);
        var untouched = await verifyContext.ApprovalActions.AsNoTracking().SingleAsync(a => a.Id == actionId);
        Assert.Equal("Original, correct comment.", untouched.Comment);
    }

    [Fact]
    public async Task With_The_Guard_Deleting_An_ApprovalAction_Throws_And_The_Row_Survives()
    {
        var (tenantId, actionId, databaseName) = await SeedOneApprovalActionAsync(withGuard: true);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using (var deleteContext = CreateContext(databaseName, tenantProvider, withGuard: true))
        {
            var action = await deleteContext.ApprovalActions.SingleAsync(a => a.Id == actionId);
            deleteContext.ApprovalActions.Remove(action);

            await Assert.ThrowsAsync<InvalidOperationException>(() => deleteContext.SaveChangesAsync());
        }

        using var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: true);
        Assert.Equal(1, await verifyContext.ApprovalActions.CountAsync());
    }

    /// <summary>
    /// S9-SEC-02 finding N-01. After the H-01 fix, <see cref="PaymentCertificateApprovalStep"/> is
    /// the <b>sole</b> record of who may approve which step, so rewriting one rung is a direct
    /// authority escalation — the re-verification proved it by editing a rung through an ordinary
    /// <c>DbContext</c> and then having a <c>Site</c> user certify a ฿9,000,000 certificate the
    /// tenant's DoA reserved for a Project Director.
    ///
    /// <para>It cannot be <c>IAppendOnly</c>, because <c>ReturnForRevision</c>/<c>Withdraw</c>
    /// legitimately delete the whole snapshot so a resubmission re-resolves a fresh chain. Hence the
    /// narrower <c>INeverModified</c> marker: <c>Added</c> and <c>Deleted</c> stay legal,
    /// <c>Modified</c> does not. Both halves are asserted here — blocking the edit is worthless if
    /// it also broke the legitimate clear-and-rebuild lifecycle.</para>
    /// </summary>
    private static async Task<(Guid TenantId, Guid CertificateId, string DatabaseName)> SeedSubmittedCertificateAsync()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var databaseName = Guid.NewGuid().ToString();

        using var context = CreateContext(databaseName, tenantProvider, withGuard: true);
        var certificate = new PaymentCertificate(tenantId, Guid.NewGuid(), 1, "IPC 1", 9_000_000.00m, 0m, Guid.NewGuid());
        certificate.SetPeriodClaim(100m, null, null, 9_000_000.00m, 450_000.00m, 900_000.00m, 7_650_000.00m);
        certificate.Submit(
            [new PaymentCertificateApprovalStepInput(1, UserRole.ProjectDirector, 1)],
            Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);
        context.PaymentCertificates.Add(certificate);
        await context.SaveChangesAsync();

        return (tenantId, certificate.Id, databaseName);
    }

    [Fact]
    public async Task An_Approval_Chain_Rung_Cannot_Be_Rewritten_To_Grant_A_Different_Role_Step_Authority()
    {
        var (tenantId, certificateId, databaseName) = await SeedSubmittedCertificateAsync();
        var tenantProvider = new FakeTenantProvider(tenantId);

        using (var tamperContext = CreateContext(databaseName, tenantProvider, withGuard: true))
        {
            var step = await tamperContext.Set<PaymentCertificateApprovalStep>()
                .SingleAsync(s => s.PaymentCertificateId == certificateId);

            // The exact escalation the re-verification demonstrated: downgrade the required role so
            // a Site user could clear a Project-Director-only step.
            tamperContext.Entry(step).Property(nameof(PaymentCertificateApprovalStep.RequiredRole)).CurrentValue =
                UserRole.Site;

            await Assert.ThrowsAsync<InvalidOperationException>(() => tamperContext.SaveChangesAsync());
        }

        using var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: true);
        var unchanged = await verifyContext.Set<PaymentCertificateApprovalStep>()
            .SingleAsync(s => s.PaymentCertificateId == certificateId);
        Assert.Equal(UserRole.ProjectDirector, unchanged.RequiredRole);
    }

    [Fact]
    public async Task Voiding_The_Chain_Snapshot_Still_Works_Deleting_A_Rung_Is_Legal_Unlike_Editing_One()
    {
        // The guard must not break ReturnForRevision, which is the whole reason this entity is
        // INeverModified rather than IAppendOnly.
        var (tenantId, certificateId, databaseName) = await SeedSubmittedCertificateAsync();
        var tenantProvider = new FakeTenantProvider(tenantId);

        using (var returnContext = CreateContext(databaseName, tenantProvider, withGuard: true))
        {
            var certificate = await returnContext.PaymentCertificates
                .Include(c => c.ApprovalSteps)
                .SingleAsync(c => c.Id == certificateId);

            certificate.ReturnForRevision();

            await returnContext.SaveChangesAsync(); // must NOT throw - Deleted is legal here.
        }

        using var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: true);
        Assert.Empty(await verifyContext.Set<PaymentCertificateApprovalStep>()
            .Where(s => s.PaymentCertificateId == certificateId)
            .ToListAsync());
    }

    // ---- M-01, sprint-10 security review: VariationOrderScopeItem freeze ----

    private static async Task<(Guid TenantId, Guid VariationOrderId, Guid ScopeItemId, string DatabaseName)> SeedSubmittedVoWithScopeItemAsync(bool withGuard)
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var databaseName = Guid.NewGuid().ToString();

        using var context = CreateContext(databaseName, tenantProvider, withGuard);
        var vo = new VariationOrder(
            tenantId, Guid.NewGuid(), "VO-1", Guid.NewGuid(), 800_000.00m, "Test VO", null, 0,
            [new VariationOrderScopeItemInput(Guid.NewGuid(), 800_000.00m)]);
        vo.Submit([new VariationOrderApprovalStepInput(1, UserRole.PM, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);
        context.VariationOrders.Add(vo);
        await context.SaveChangesAsync();

        return (tenantId, vo.Id, vo.ScopeItems.Single().Id, databaseName);
    }

    /// <summary>
    /// M-01 (sprint-10 security review), reproduces the review's own probe exactly: a raw
    /// <c>BudgetCostDelta</c> rewrite on a <c>PendingApproval</c> VO's scope item persisted with no
    /// error at all - the eventual approval would then move <c>Project.BAC</c> by the VO's own
    /// (correctly frozen) <c>Amount</c> while moving the target <c>Activity.BudgetCost</c> by the
    /// TAMPERED delta, permanently desynchronising $EV$/$PV$ from $BAC$ with no test on
    /// <c>Project.BAC</c> ever able to notice (domain-rules.md §5.2's named nightmare). The baseline
    /// this fix closes. Kept as a permanent regression test for the same reason its
    /// <see cref="PaymentCertificateApprovalStep"/> sibling above is.
    /// </summary>
    [Fact]
    public async Task Without_The_Guard_A_PendingApproval_Vos_Scope_Item_BudgetCostDelta_Can_Still_Be_Rewritten_Through_An_Ordinary_DbContext()
    {
        var (tenantId, _, scopeItemId, databaseName) = await SeedSubmittedVoWithScopeItemAsync(withGuard: false);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using (var tamperContext = CreateContext(databaseName, tenantProvider, withGuard: false))
        {
            var scopeItem = await tamperContext.Set<VariationOrderScopeItem>().SingleAsync(s => s.Id == scopeItemId);
            tamperContext.Entry(scopeItem).Property(nameof(VariationOrderScopeItem.BudgetCostDelta)).CurrentValue = 44_000_000.00m;
            await tamperContext.SaveChangesAsync(); // succeeds today - the bug.
        }

        using var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: false);
        var tampered = await verifyContext.Set<VariationOrderScopeItem>().AsNoTracking().SingleAsync(s => s.Id == scopeItemId);
        Assert.Equal(44_000_000.00m, tampered.BudgetCostDelta);
    }

    [Fact]
    public async Task With_The_Guard_A_PendingApproval_Vos_Scope_Item_BudgetCostDelta_Cannot_Be_Rewritten_Through_An_Ordinary_DbContext()
    {
        var (tenantId, _, scopeItemId, databaseName) = await SeedSubmittedVoWithScopeItemAsync(withGuard: true);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using (var tamperContext = CreateContext(databaseName, tenantProvider, withGuard: true))
        {
            var scopeItem = await tamperContext.Set<VariationOrderScopeItem>().SingleAsync(s => s.Id == scopeItemId);
            tamperContext.Entry(scopeItem).Property(nameof(VariationOrderScopeItem.BudgetCostDelta)).CurrentValue = 44_000_000.00m;

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => tamperContext.SaveChangesAsync());
            Assert.Contains(nameof(VariationOrderScopeItem), ex.Message);
        }

        using var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: true);
        var untouched = await verifyContext.Set<VariationOrderScopeItem>().AsNoTracking().SingleAsync(s => s.Id == scopeItemId);
        Assert.Equal(800_000.00m, untouched.BudgetCostDelta); // unchanged
    }

    /// <summary>
    /// The guard must not break the legitimate lifecycle: <c>SetVariationContent</c> (invoked here via
    /// <c>ReturnForRevision</c> unfreezing the document, then a re-price) clears and re-adds the whole
    /// <c>ScopeItems</c> collection - <c>Added</c>/<c>Deleted</c> must stay legal, only editing an
    /// existing row in place is blocked. Mirrors <see cref="Voiding_The_Chain_Snapshot_Still_Works_Deleting_A_Rung_Is_Legal_Unlike_Editing_One"/>'s
    /// proof for the sibling <see cref="PaymentCertificateApprovalStep"/> shape.
    /// </summary>
    [Fact]
    public async Task Rewriting_A_Vos_Scope_Items_Via_Return_And_Re_Price_Still_Works_Delete_Then_Add_Is_Legal_Unlike_Editing()
    {
        var (tenantId, voId, _, databaseName) = await SeedSubmittedVoWithScopeItemAsync(withGuard: true);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using (var returnContext = CreateContext(databaseName, tenantProvider, withGuard: true))
        {
            var vo = await returnContext.VariationOrders
                .Include(v => v.ApprovalSteps)
                .Include(v => v.ScopeItems)
                .SingleAsync(v => v.Id == voId);

            vo.ReturnForRevision();
            vo.SetVariationContent(500_000.00m, "Re-priced", null, 0, [new VariationOrderScopeItemInput(Guid.NewGuid(), 500_000.00m)]);

            await returnContext.SaveChangesAsync(); // must NOT throw - the old row is Deleted, a new one Added.
        }

        using var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: true);
        var scopeItems = await verifyContext.Set<VariationOrderScopeItem>()
            .Where(s => s.VariationOrderId == voId)
            .ToListAsync();
        var reloadedScopeItem = Assert.Single(scopeItems);
        Assert.Equal(500_000.00m, reloadedScopeItem.BudgetCostDelta);
    }

    [Fact]
    public async Task With_The_Guard_Adding_A_New_ApprovalAction_Still_Succeeds_Append_Is_Still_Allowed()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var databaseName = Guid.NewGuid().ToString();

        using var context = CreateContext(databaseName, tenantProvider, withGuard: true);
        var action = new ApprovalAction(
            tenantId, ApprovalDocumentType.PaymentCertificate, Guid.NewGuid(), revisionNo: 1, stepNo: 1,
            Guid.NewGuid(), UserRole.QS, ApprovalActionType.Approve, comment: "Fine.", Now, Guid.NewGuid(), 1);
        context.ApprovalActions.Add(action);

        await context.SaveChangesAsync(); // must not throw - Added is not Modified/Deleted.

        Assert.Equal(1, await context.ApprovalActions.CountAsync());
    }

    // ---- M-01 money-freeze half (security review sprint-09.md §9.4/§9.5) ----

    private static async Task<(Guid TenantId, Guid CertificateId, string DatabaseName)> SeedCertifiedCertificateAsync(bool withGuard)
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var databaseName = Guid.NewGuid().ToString();

        using var context = CreateContext(databaseName, tenantProvider, withGuard);
        var certificate = new PaymentCertificate(tenantId, Guid.NewGuid(), 1, "IPC 1", 21_600_000.00m, 0m, Guid.NewGuid());
        certificate.SetPeriodClaim(100m, null, null, 21_600_000.00m, 1_080_000.00m, 2_160_000.00m, 18_360_000.00m);
        certificate.Submit([new PaymentCertificateApprovalStepInput(1, UserRole.QS, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);
        certificate.Approve(Guid.NewGuid(), UserRole.QS, UserRole.QS, allowSelfApproval: false, Now);
        Assert.Equal(PaymentCertificateStatus.Certified, certificate.Status);

        context.PaymentCertificates.Add(certificate);
        await context.SaveChangesAsync();

        return (tenantId, certificate.Id, databaseName);
    }

    private static async Task<(Guid TenantId, Guid CertificateId, string DatabaseName)> SeedPendingApprovalCertificateAsync()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var databaseName = Guid.NewGuid().ToString();

        using var context = CreateContext(databaseName, tenantProvider, withGuard: true);
        var certificate = new PaymentCertificate(tenantId, Guid.NewGuid(), 1, "IPC 1", 21_600_000.00m, 0m, Guid.NewGuid());
        certificate.SetPeriodClaim(100m, null, null, 21_600_000.00m, 1_080_000.00m, 2_160_000.00m, 18_360_000.00m);
        certificate.Submit([new PaymentCertificateApprovalStepInput(1, UserRole.QS, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);
        Assert.Equal(PaymentCertificateStatus.PendingApproval, certificate.Status);

        context.PaymentCertificates.Add(certificate);
        await context.SaveChangesAsync();

        return (tenantId, certificate.Id, databaseName);
    }

    /// <summary>
    /// Reproduces the exact gap this task's handoff report re-confirmed live: a <c>Certified</c>
    /// certificate's <c>NetPayment</c> could still be rewritten to 999,999,999.00 through an ordinary
    /// <see cref="CmPlusDbContext"/> with no error at all - the baseline this half of the fix closes.
    /// Kept as a permanent regression test for the same reason its <c>ApprovalAction</c> sibling above
    /// is: so nobody can reintroduce a code path that constructs the DbContext without the guard and
    /// silently regress.
    /// </summary>
    [Fact]
    public async Task Without_The_Guard_A_Certified_Certificates_NetPayment_Can_Still_Be_Rewritten_Through_An_Ordinary_DbContext()
    {
        var (tenantId, certificateId, databaseName) = await SeedCertifiedCertificateAsync(withGuard: false);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using (var tamperContext = CreateContext(databaseName, tenantProvider, withGuard: false))
        {
            var certificate = await tamperContext.PaymentCertificates.SingleAsync(c => c.Id == certificateId);
            tamperContext.Entry(certificate).Property(nameof(PaymentCertificate.NetPayment)).CurrentValue = 999_999_999.00m;
            await tamperContext.SaveChangesAsync(); // succeeds today - the bug.
        }

        using var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: false);
        var tampered = await verifyContext.PaymentCertificates.AsNoTracking().SingleAsync(c => c.Id == certificateId);
        Assert.Equal(999_999_999.00m, tampered.NetPayment);
    }

    /// <summary>
    /// The fix, for every money field the aggregate's own <c>SetPeriodClaim</c> doc comment names -
    /// not just <c>NetPayment</c> - once the certificate has reached <c>Certified</c> (well past the
    /// freeze boundary).
    /// </summary>
    [Theory]
    [InlineData(nameof(PaymentCertificate.ApprovePct))]
    [InlineData(nameof(PaymentCertificate.GrossCertifiedAmount))]
    [InlineData(nameof(PaymentCertificate.RetentionAmount))]
    [InlineData(nameof(PaymentCertificate.AdvanceRecoveryAmount))]
    [InlineData(nameof(PaymentCertificate.NetPayment))]
    public async Task With_The_Guard_A_Certified_Certificates_Money_Field_Cannot_Be_Rewritten_Through_An_Ordinary_DbContext(string propertyName)
    {
        var (tenantId, certificateId, databaseName) = await SeedCertifiedCertificateAsync(withGuard: true);
        var tenantProvider = new FakeTenantProvider(tenantId);

        var originalValue = (decimal)typeof(PaymentCertificate).GetProperty(propertyName)!.GetValue(
            await LoadUntrackedAsync(databaseName, tenantProvider, certificateId))!;

        using (var tamperContext = CreateContext(databaseName, tenantProvider, withGuard: true))
        {
            var certificate = await tamperContext.PaymentCertificates.SingleAsync(c => c.Id == certificateId);
            tamperContext.Entry(certificate).Property(propertyName).CurrentValue = originalValue + 999_999_999.00m;

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => tamperContext.SaveChangesAsync());
            Assert.Contains(propertyName, ex.Message);
            Assert.Contains("Certified", ex.Message);
        }

        var untouched = await LoadUntrackedAsync(databaseName, tenantProvider, certificateId);
        Assert.Equal(originalValue, (decimal)typeof(PaymentCertificate).GetProperty(propertyName)!.GetValue(untouched)!);
    }

    /// <summary>Freeze begins at <c>PendingApproval</c>, not only at <c>Certified</c> - matching
    /// <c>EnsureMoneyFieldsEditable</c>'s own boundary exactly (anything other than
    /// <c>NotDue</c>/<c>Draft</c>).</summary>
    [Fact]
    public async Task With_The_Guard_A_PendingApproval_Certificates_Money_Field_Cannot_Be_Rewritten_Through_An_Ordinary_DbContext()
    {
        var (tenantId, certificateId, databaseName) = await SeedPendingApprovalCertificateAsync();
        var tenantProvider = new FakeTenantProvider(tenantId);

        using var tamperContext = CreateContext(databaseName, tenantProvider, withGuard: true);
        var certificate = await tamperContext.PaymentCertificates.SingleAsync(c => c.Id == certificateId);
        tamperContext.Entry(certificate).Property(nameof(PaymentCertificate.GrossCertifiedAmount)).CurrentValue = 1.00m;

        await Assert.ThrowsAsync<InvalidOperationException>(() => tamperContext.SaveChangesAsync());
    }

    /// <summary>
    /// The guard must not block the legitimate happy path: a <c>Draft</c> certificate's money fields
    /// are still fully editable (mirroring what a future re-price command handler would do by calling
    /// <see cref="PaymentCertificate.SetPeriodClaim"/> against a tracked, guard-wired context - no
    /// production handler exists yet, so this exercises the Domain method directly the same way
    /// <c>PaymentCertificateConcurrencyTests.RowVersion_Changes_On_Every_Persisted_Mutation</c> does).
    /// </summary>
    [Fact]
    public async Task With_The_Guard_A_Drafts_Money_Fields_Can_Still_Be_Legitimately_Repriced_Through_An_Ordinary_DbContext()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var databaseName = Guid.NewGuid().ToString();

        Guid certificateId;
        using (var seedContext = CreateContext(databaseName, tenantProvider, withGuard: true))
        {
            var certificate = new PaymentCertificate(tenantId, Guid.NewGuid(), 1, "IPC 1", 10_000_000.00m, 0m, Guid.NewGuid());
            seedContext.PaymentCertificates.Add(certificate);
            await seedContext.SaveChangesAsync();
            certificateId = certificate.Id;
        }

        using (var repriceContext = CreateContext(databaseName, tenantProvider, withGuard: true))
        {
            var certificate = await repriceContext.PaymentCertificates.SingleAsync(c => c.Id == certificateId);
            Assert.Equal(PaymentCertificateStatus.Draft, certificate.Status);
            certificate.SetPeriodClaim(60m, null, null, 6_000_000.00m, 300_000.00m, 600_000.00m, 5_100_000.00m);

            await repriceContext.SaveChangesAsync(); // must NOT throw - this is the whole point of the guard.
        }

        var reloaded = await LoadUntrackedAsync(databaseName, tenantProvider, certificateId);
        Assert.Equal(6_000_000.00m, reloaded.GrossCertifiedAmount);
        Assert.Equal(5_100_000.00m, reloaded.NetPayment);
    }

    /// <summary>
    /// The other half of "must not break the happy path": a legitimate status/step transition that
    /// happens to run once the certificate has already left Draft (here, <c>RecordPayment</c> on a
    /// <c>Certified</c> certificate) must not be blocked merely because <c>Status</c> is frozen-zone -
    /// the guard keys off which properties are actually <see cref="Microsoft.EntityFrameworkCore.ChangeTracking.PropertyEntry.IsModified"/>,
    /// not off the row being touched at all.
    /// </summary>
    [Fact]
    public async Task With_The_Guard_Recording_Payment_On_A_Certified_Certificate_Still_Succeeds_A_Status_Only_Transition_Is_Not_Blocked()
    {
        var (tenantId, certificateId, databaseName) = await SeedCertifiedCertificateAsync(withGuard: true);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using (var recordPaymentContext = CreateContext(databaseName, tenantProvider, withGuard: true))
        {
            var certificate = await recordPaymentContext.PaymentCertificates.SingleAsync(c => c.Id == certificateId);
            certificate.RecordPayment("TT-2026-001", Now);

            await recordPaymentContext.SaveChangesAsync(); // must NOT throw - Status changed, money fields did not.
        }

        var paid = await LoadUntrackedAsync(databaseName, tenantProvider, certificateId);
        Assert.Equal(PaymentCertificateStatus.Paid, paid.Status);
        Assert.Equal(18_360_000.00m, paid.NetPayment); // untouched by the status-only transition
    }

    private static async Task<PaymentCertificate> LoadUntrackedAsync(string databaseName, FakeTenantProvider tenantProvider, Guid certificateId)
    {
        using var context = CreateContext(databaseName, tenantProvider, withGuard: true);
        return await context.PaymentCertificates.AsNoTracking().SingleAsync(c => c.Id == certificateId);
    }

    // ---- S11-BE-01: DailyWeatherLog immutability (this task's brief: "prove the immutability
    // guarantee the way Sprint 9 did - a raw DbContext rewrite/delete attempt that is blocked, plus
    // one confirming legitimate appends still work") ----

    private static async Task<(Guid TenantId, Guid LogId, Guid ActivityId, string DatabaseName)> SeedOneWeatherLogWithAffectedActivityAsync(bool withGuard)
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var databaseName = Guid.NewGuid().ToString();
        var activityId = Guid.NewGuid();

        using var context = CreateContext(databaseName, tenantProvider, withGuard);
        var log = DailyWeatherLog.CreateOriginal(
            tenantId, Guid.NewGuid(), DateTimeOffset.Parse("2026-07-11T00:00:00+07:00"), WeatherCondition.HeavyRain,
            "ฝนตกหนัก", 42.5m, WeatherImpact.FullStoppage, "หยุดเทคอนกรีตโซน B ครึ่งวัน", 8.00m, Guid.NewGuid(),
            DateTimeOffset.Parse("2026-07-11T18:00:00+07:00"), [activityId]);
        context.DailyWeatherLogs.Add(log);
        await context.SaveChangesAsync();

        return (tenantId, log.Id, activityId, databaseName);
    }

    /// <summary>
    /// Reproduces the M-01 shape exactly (review probe 7, reproduced for this new entity): a
    /// <c>DailyWeatherLog</c> row can be rewritten and deleted through an ordinary
    /// <see cref="CmPlusDbContext"/> with no guard interceptor wired in - the baseline S11-BE-01's
    /// use of <see cref="IAppendOnly"/> closes. Kept as a permanent regression test for the same
    /// reason its <see cref="ApprovalAction"/>/<see cref="VariationOrderScopeItem"/> siblings above
    /// are.
    /// </summary>
    [Fact]
    public async Task Without_The_Guard_A_DailyWeatherLogs_Condition_Can_Still_Be_Rewritten_And_Deleted_Through_An_Ordinary_DbContext()
    {
        var (tenantId, logId, _, databaseName) = await SeedOneWeatherLogWithAffectedActivityAsync(withGuard: false);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using (var tamperContext = CreateContext(databaseName, tenantProvider, withGuard: false))
        {
            var log = await tamperContext.DailyWeatherLogs.SingleAsync(w => w.Id == logId);
            tamperContext.Entry(log).Property(nameof(DailyWeatherLog.ConditionNote)).CurrentValue = "TAMPERED - แดดจัด (fabricated, no rain at all)";
            tamperContext.Entry(log).Property(nameof(DailyWeatherLog.RainfallMm)).CurrentValue = 0.00m;
            tamperContext.Entry(log).Property(nameof(DailyWeatherLog.Impact)).CurrentValue = WeatherImpact.NoImpact;
            await tamperContext.SaveChangesAsync(); // succeeds today - the bug this fix closes.
        }

        using (var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: false))
        {
            var tampered = await verifyContext.DailyWeatherLogs.AsNoTracking().SingleAsync(w => w.Id == logId);
            Assert.StartsWith("TAMPERED", tampered.ConditionNote);
            Assert.Equal(WeatherImpact.NoImpact, tampered.Impact);
        }

        using (var deleteContext = CreateContext(databaseName, tenantProvider, withGuard: false))
        {
            // Delete the child row first (no guard yet to stop it either) so the FK does not block
            // the parent delete below - isolating this probe to what the append-only guard itself
            // is responsible for, not incidental referential integrity.
            deleteContext.RemoveRange(deleteContext.Set<DailyWeatherLogActivity>().Where(a => a.DailyWeatherLogId == logId));
            await deleteContext.SaveChangesAsync();
        }

        using (var deleteContext = CreateContext(databaseName, tenantProvider, withGuard: false))
        {
            var log = await deleteContext.DailyWeatherLogs.SingleAsync(w => w.Id == logId);
            deleteContext.DailyWeatherLogs.Remove(log);
            await deleteContext.SaveChangesAsync(); // succeeds today - the bug this fix closes.
        }

        using (var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: false))
        {
            Assert.Equal(0, await verifyContext.DailyWeatherLogs.CountAsync());
        }
    }

    /// <summary>The fix: a legal-evidence weather entry's fields cannot be rewritten through an
    /// ordinary <see cref="CmPlusDbContext"/> once the guard is wired in - this is what "no update
    /// endpoint" actually rests on structurally, not merely on nobody writing one (S11-BE-01 DoD).</summary>
    [Theory]
    [InlineData(nameof(DailyWeatherLog.ConditionNote))]
    [InlineData(nameof(DailyWeatherLog.RainfallMm))]
    [InlineData(nameof(DailyWeatherLog.Impact))]
    [InlineData(nameof(DailyWeatherLog.HoursLost))]
    public async Task With_The_Guard_A_DailyWeatherLogs_Field_Cannot_Be_Rewritten_Through_An_Ordinary_DbContext(string propertyName)
    {
        var (tenantId, logId, _, databaseName) = await SeedOneWeatherLogWithAffectedActivityAsync(withGuard: true);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using var tamperContext = CreateContext(databaseName, tenantProvider, withGuard: true);
        var log = await tamperContext.DailyWeatherLogs.SingleAsync(w => w.Id == logId);

        object tamperedValue = propertyName switch
        {
            nameof(DailyWeatherLog.ConditionNote) => "TAMPERED",
            nameof(DailyWeatherLog.RainfallMm) => 999.99m,
            nameof(DailyWeatherLog.Impact) => WeatherImpact.NoImpact,
            nameof(DailyWeatherLog.HoursLost) => 0.50m,
            _ => throw new InvalidOperationException($"Unhandled property {propertyName}"),
        };
        tamperContext.Entry(log).Property(propertyName).CurrentValue = tamperedValue;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => tamperContext.SaveChangesAsync());
        Assert.Contains(nameof(DailyWeatherLog), ex.Message);
        Assert.Contains("append-only", ex.Message);
    }

    [Fact]
    public async Task With_The_Guard_Deleting_A_DailyWeatherLog_Throws_And_The_Row_Survives()
    {
        var (tenantId, logId, activityId, databaseName) = await SeedOneWeatherLogWithAffectedActivityAsync(withGuard: true);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using (var deleteContext = CreateContext(databaseName, tenantProvider, withGuard: true))
        {
            var log = await deleteContext.DailyWeatherLogs.SingleAsync(w => w.Id == logId);
            deleteContext.DailyWeatherLogs.Remove(log);

            await Assert.ThrowsAsync<InvalidOperationException>(() => deleteContext.SaveChangesAsync());
        }

        using var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: true);
        Assert.Equal(1, await verifyContext.DailyWeatherLogs.CountAsync());
        var survivingLog = await verifyContext.DailyWeatherLogs.AsNoTracking().Include(w => w.AffectedActivities).SingleAsync(w => w.Id == logId);
        Assert.Equal(activityId, Assert.Single(survivingLog.AffectedActivities).ActivityId);
    }

    /// <summary>The narrower, tighter guarantee on the owned child row (see
    /// <see cref="DailyWeatherLogActivity"/>'s own remarks on why it is <see cref="IAppendOnly"/>
    /// too, unlike e.g. <see cref="VariationOrderScopeItem"/>): even the affected-activity tag
    /// itself cannot be rewritten to point at a different activity.</summary>
    [Fact]
    public async Task With_The_Guard_A_DailyWeatherLogActivitys_ActivityId_Cannot_Be_Rewritten()
    {
        var (tenantId, logId, originalActivityId, databaseName) = await SeedOneWeatherLogWithAffectedActivityAsync(withGuard: true);
        var tenantProvider = new FakeTenantProvider(tenantId);
        var substitutedActivityId = Guid.NewGuid();

        using (var tamperContext = CreateContext(databaseName, tenantProvider, withGuard: true))
        {
            var affected = await tamperContext.Set<DailyWeatherLogActivity>().SingleAsync(a => a.DailyWeatherLogId == logId);
            tamperContext.Entry(affected).Property(nameof(DailyWeatherLogActivity.ActivityId)).CurrentValue = substitutedActivityId;

            await Assert.ThrowsAsync<InvalidOperationException>(() => tamperContext.SaveChangesAsync());
        }

        using var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: true);
        var untouched = await verifyContext.Set<DailyWeatherLogActivity>().AsNoTracking().SingleAsync(a => a.DailyWeatherLogId == logId);
        Assert.Equal(originalActivityId, untouched.ActivityId);
    }

    /// <summary>The other, equally important half: the guard must not block the legitimate path.
    /// Corrections are new rows (S11-BE-01 DoD "a correction is a new entry referencing the
    /// original") - appending a second log that corrects the first must succeed exactly like an
    /// ordinary first-time append does.</summary>
    [Fact]
    public async Task With_The_Guard_Appending_A_New_WeatherLog_Still_Succeeds_Including_A_Correction_Referencing_The_Original()
    {
        var (tenantId, originalLogId, _, databaseName) = await SeedOneWeatherLogWithAffectedActivityAsync(withGuard: true);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using (var correctionContext = CreateContext(databaseName, tenantProvider, withGuard: true))
        {
            var original = await correctionContext.DailyWeatherLogs.AsNoTracking().SingleAsync(w => w.Id == originalLogId);

            var correction = DailyWeatherLog.CreateCorrection(
                tenantId, original.ProjectId, originalLogId, "ตรวจใบบันทึกกะแล้ว หยุดจริงทั้งวัน", original.LogDate,
                WeatherCondition.Storm, "ฝนตกหนักมาก (แก้ไข)", 61.00m, WeatherImpact.FullStoppage,
                "หยุดงานภายนอกทั้งวัน (ตัวเลขเดิมต่ำเกินไป)", 8.00m, Guid.NewGuid(),
                DateTimeOffset.Parse("2026-07-12T09:00:00+07:00"), []);
            correctionContext.DailyWeatherLogs.Add(correction);

            await correctionContext.SaveChangesAsync(); // must NOT throw - Added is not Modified/Deleted.
        }

        using var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: true);
        Assert.Equal(2, await verifyContext.DailyWeatherLogs.CountAsync());
        var original2 = await verifyContext.DailyWeatherLogs.AsNoTracking().SingleAsync(w => w.Id == originalLogId);
        Assert.Null(original2.CorrectsWeatherLogId); // the original itself is untouched by the later correction.
        var correction2 = await verifyContext.DailyWeatherLogs.AsNoTracking().SingleAsync(w => w.Id != originalLogId);
        Assert.Equal(originalLogId, correction2.CorrectsWeatherLogId);
    }

    // ---- ADR-0019: CpmRun/CpmRunActivity/CpmRunRelation immutability (this task's brief: prove a
    // run is genuinely immutable through a raw DbContext, the exact M-01 pattern DailyWeatherLog
    // above was built to and ApprovalAction/PaymentCertificate were retrofitted to) ----

    private static async Task<(Guid TenantId, Guid RunId, Guid ActivityRowId, Guid RelationRowId, string DatabaseName)>
        SeedOneCpmRunWithChildrenAsync(bool withGuard)
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var databaseName = Guid.NewGuid().ToString();
        var activityAId = Guid.NewGuid();
        var activityBId = Guid.NewGuid();

        using var context = CreateContext(databaseName, tenantProvider, withGuard);
        var run = CpmRun.Capture(
            tenantId, Guid.NewGuid(), Now, DateTimeOffset.Parse("2026-08-01T00:00:00+07:00"), projectDurationDays: 15,
            Guid.NewGuid(), CpmRunTrigger.Manual,
            [
                new CpmRunActivityInput(activityAId, 5, 0, 5, 0, 5, TotalFloat: 0, FreeFloat: 0, IsCritical: true),
                new CpmRunActivityInput(activityBId, 3, 5, 8, 8, 11, TotalFloat: 3, FreeFloat: 3, IsCritical: false),
            ],
            [new CpmRunRelationInput(activityAId, activityBId, RelationType.FS, LagDays: 0)]);
        context.CpmRuns.Add(run);
        await context.SaveChangesAsync();

        return (tenantId, run.Id, run.Activities.Single(a => a.ActivityId == activityAId).Id, run.Relations.Single().Id, databaseName);
    }

    /// <summary>Reproduces the M-01 shape exactly (review probe 7, reproduced for this new
    /// aggregate): a <c>CpmRun</c> row and its children can be rewritten and deleted through an
    /// ordinary <see cref="CmPlusDbContext"/> with no guard interceptor wired in - the baseline
    /// ADR-0019's use of <see cref="IAppendOnly"/> closes. Kept as a permanent regression test for
    /// the same reason its <see cref="DailyWeatherLog"/>/<see cref="ApprovalAction"/> siblings above
    /// are.</summary>
    [Fact]
    public async Task Without_The_Guard_A_CpmRuns_ProjectDurationDays_Can_Still_Be_Rewritten_And_Deleted_Through_An_Ordinary_DbContext()
    {
        var (tenantId, runId, _, _, databaseName) = await SeedOneCpmRunWithChildrenAsync(withGuard: false);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using (var tamperContext = CreateContext(databaseName, tenantProvider, withGuard: false))
        {
            var run = await tamperContext.CpmRuns.SingleAsync(r => r.Id == runId);
            tamperContext.Entry(run).Property(nameof(CpmRun.ProjectDurationDays)).CurrentValue = 999;
            await tamperContext.SaveChangesAsync(); // succeeds today - the bug this fix closes.
        }

        using (var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: false))
        {
            var tampered = await verifyContext.CpmRuns.AsNoTracking().SingleAsync(r => r.Id == runId);
            Assert.Equal(999, tampered.ProjectDurationDays);
        }

        using (var deleteContext = CreateContext(databaseName, tenantProvider, withGuard: false))
        {
            // Delete children first (no guard yet to stop it either) so the FK does not block the
            // parent delete below - isolating this probe to what the append-only guard itself is
            // responsible for, not incidental referential integrity.
            deleteContext.RemoveRange(deleteContext.Set<CpmRunActivity>().Where(a => a.CpmRunId == runId));
            deleteContext.RemoveRange(deleteContext.Set<CpmRunRelation>().Where(r => r.CpmRunId == runId));
            await deleteContext.SaveChangesAsync();
        }

        using (var deleteContext = CreateContext(databaseName, tenantProvider, withGuard: false))
        {
            var run = await deleteContext.CpmRuns.SingleAsync(r => r.Id == runId);
            deleteContext.CpmRuns.Remove(run);
            await deleteContext.SaveChangesAsync(); // succeeds today - the bug this fix closes.
        }

        using (var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: false))
        {
            Assert.Equal(0, await verifyContext.CpmRuns.CountAsync());
        }
    }

    [Fact]
    public async Task With_The_Guard_A_CpmRuns_ProjectDurationDays_Cannot_Be_Rewritten_Through_An_Ordinary_DbContext()
    {
        var (tenantId, runId, _, _, databaseName) = await SeedOneCpmRunWithChildrenAsync(withGuard: true);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using var tamperContext = CreateContext(databaseName, tenantProvider, withGuard: true);
        var run = await tamperContext.CpmRuns.SingleAsync(r => r.Id == runId);
        tamperContext.Entry(run).Property(nameof(CpmRun.ProjectDurationDays)).CurrentValue = 999;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => tamperContext.SaveChangesAsync());
        Assert.Contains(nameof(CpmRun), ex.Message);
        Assert.Contains("append-only", ex.Message);

        using var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: true);
        var untouched = await verifyContext.CpmRuns.AsNoTracking().SingleAsync(r => r.Id == runId);
        Assert.Equal(15, untouched.ProjectDurationDays);
    }

    [Fact]
    public async Task With_The_Guard_Deleting_A_CpmRun_Throws_And_The_Row_Survives()
    {
        var (tenantId, runId, _, _, databaseName) = await SeedOneCpmRunWithChildrenAsync(withGuard: true);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using (var deleteContext = CreateContext(databaseName, tenantProvider, withGuard: true))
        {
            var run = await deleteContext.CpmRuns.SingleAsync(r => r.Id == runId);
            deleteContext.CpmRuns.Remove(run);

            await Assert.ThrowsAsync<InvalidOperationException>(() => deleteContext.SaveChangesAsync());
        }

        using var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: true);
        Assert.Equal(1, await verifyContext.CpmRuns.CountAsync());
        var surviving = await verifyContext.CpmRuns.AsNoTracking()
            .Include(r => r.Activities).Include(r => r.Relations).SingleAsync(r => r.Id == runId);
        Assert.Equal(2, surviving.Activities.Count);
        Assert.Single(surviving.Relations);
    }

    /// <summary>The narrower, tighter guarantee on the owned children (mirrors
    /// <c>With_The_Guard_A_DailyWeatherLogActivitys_ActivityId_Cannot_Be_Rewritten</c> above):
    /// even a single <see cref="CpmRunActivity"/>/<see cref="CpmRunRelation"/> row cannot be
    /// rewritten in isolation, without touching the parent <see cref="CpmRun"/> at all - a captured
    /// run is immutable in its entirety, not merely field-by-field on the parent row.</summary>
    [Fact]
    public async Task With_The_Guard_A_CpmRunActivitys_TotalFloat_Cannot_Be_Rewritten()
    {
        var (tenantId, _, activityRowId, _, databaseName) = await SeedOneCpmRunWithChildrenAsync(withGuard: true);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using (var tamperContext = CreateContext(databaseName, tenantProvider, withGuard: true))
        {
            var runActivity = await tamperContext.Set<CpmRunActivity>().SingleAsync(a => a.Id == activityRowId);
            tamperContext.Entry(runActivity).Property(nameof(CpmRunActivity.TotalFloat)).CurrentValue = 999;

            await Assert.ThrowsAsync<InvalidOperationException>(() => tamperContext.SaveChangesAsync());
        }

        using var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: true);
        var untouched = await verifyContext.Set<CpmRunActivity>().AsNoTracking().SingleAsync(a => a.Id == activityRowId);
        Assert.Equal(0, untouched.TotalFloat);
    }

    [Fact]
    public async Task With_The_Guard_A_CpmRunRelations_RelationType_Cannot_Be_Rewritten()
    {
        var (tenantId, _, _, relationRowId, databaseName) = await SeedOneCpmRunWithChildrenAsync(withGuard: true);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using (var tamperContext = CreateContext(databaseName, tenantProvider, withGuard: true))
        {
            var runRelation = await tamperContext.Set<CpmRunRelation>().SingleAsync(r => r.Id == relationRowId);
            tamperContext.Entry(runRelation).Property(nameof(CpmRunRelation.RelationType)).CurrentValue = RelationType.FF;

            await Assert.ThrowsAsync<InvalidOperationException>(() => tamperContext.SaveChangesAsync());
        }

        using var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: true);
        var untouched = await verifyContext.Set<CpmRunRelation>().AsNoTracking().SingleAsync(r => r.Id == relationRowId);
        Assert.Equal(RelationType.FS, untouched.RelationType);
    }

    /// <summary>The other, equally important half: the guard must not block the legitimate path -
    /// capturing a brand new run (the only production write path, via
    /// <c>RecalculateCpmCommandHandler</c>) must still succeed exactly like any other append.</summary>
    [Fact]
    public async Task With_The_Guard_Adding_A_New_CpmRun_Still_Succeeds_Append_Is_Still_Allowed()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var databaseName = Guid.NewGuid().ToString();
        var activityId = Guid.NewGuid();

        using var context = CreateContext(databaseName, tenantProvider, withGuard: true);
        var run = CpmRun.Capture(
            tenantId, Guid.NewGuid(), Now, null, projectDurationDays: 0, Guid.NewGuid(), CpmRunTrigger.Manual, [], []);
        context.CpmRuns.Add(run);

        await context.SaveChangesAsync(); // must not throw - Added is not Modified/Deleted.

        Assert.Equal(1, await context.CpmRuns.CountAsync());
    }

    // ---- S14-BE-01: Baseline/BaselineActivitySnapshot immutability ("Baseline snapshots are
    // historical evidence" - this task's brief; mirrors the CpmRun/CpmRunActivity M-01 pattern
    // exactly, for the child row only - Baseline itself is deliberately NOT IAppendOnly, since
    // IsActive must stay mutable for the project's whole life, so this section also confirms that
    // legitimate mutation still works under the guard) ----

    private static async Task<(Guid TenantId, Guid BaselineId, Guid SnapshotRowId, string DatabaseName)>
        SeedOneBaselineWithASnapshotAsync(bool withGuard)
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var databaseName = Guid.NewGuid().ToString();
        var activityId = Guid.NewGuid();

        using var context = CreateContext(databaseName, tenantProvider, withGuard);
        var baseline = Domain.Entities.Baseline.Capture(
            tenantId, Guid.NewGuid(), "Revised Baseline - Rev.2", Now, Guid.NewGuid(), 492_400_000.00m,
            [new BaselineActivitySnapshotInput(activityId, Now, Now.AddDays(10), 10, 1_000_000.00m)]);
        context.Baselines.Add(baseline);
        await context.SaveChangesAsync();

        return (tenantId, baseline.Id, baseline.Snapshots.Single().Id, databaseName);
    }

    /// <summary>Reproduces the M-01 shape exactly (review probe 7, reproduced for this new child
    /// row): a <see cref="BaselineActivitySnapshot"/> row can be rewritten and deleted through an
    /// ordinary <see cref="CmPlusDbContext"/> with no guard interceptor wired in - the baseline
    /// S14-BE-01's use of <see cref="IAppendOnly"/> closes. Kept as a permanent regression test for
    /// the same reason its <see cref="CpmRunActivity"/>/<see cref="DailyWeatherLog"/> siblings above
    /// are.</summary>
    [Fact]
    public async Task Without_The_Guard_A_BaselineActivitySnapshots_BudgetCost_Can_Still_Be_Rewritten_And_Deleted_Through_An_Ordinary_DbContext()
    {
        var (tenantId, _, snapshotRowId, databaseName) = await SeedOneBaselineWithASnapshotAsync(withGuard: false);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using (var tamperContext = CreateContext(databaseName, tenantProvider, withGuard: false))
        {
            var snapshot = await tamperContext.Set<BaselineActivitySnapshot>().SingleAsync(s => s.Id == snapshotRowId);
            tamperContext.Entry(snapshot).Property(nameof(BaselineActivitySnapshot.BudgetCost)).CurrentValue = 999_999_999.00m;
            await tamperContext.SaveChangesAsync(); // succeeds today - the bug this fix closes.
        }

        using (var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: false))
        {
            var tampered = await verifyContext.Set<BaselineActivitySnapshot>().AsNoTracking().SingleAsync(s => s.Id == snapshotRowId);
            Assert.Equal(999_999_999.00m, tampered.BudgetCost);
        }

        using (var deleteContext = CreateContext(databaseName, tenantProvider, withGuard: false))
        {
            var snapshot = await deleteContext.Set<BaselineActivitySnapshot>().SingleAsync(s => s.Id == snapshotRowId);
            deleteContext.Remove(snapshot);
            await deleteContext.SaveChangesAsync(); // succeeds today - the bug this fix closes.
        }

        using (var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: false))
        {
            Assert.Equal(0, await verifyContext.Set<BaselineActivitySnapshot>().CountAsync());
        }
    }

    [Theory]
    [InlineData(nameof(BaselineActivitySnapshot.BudgetCost))]
    [InlineData(nameof(BaselineActivitySnapshot.DurationDays))]
    [InlineData(nameof(BaselineActivitySnapshot.PlannedStart))]
    public async Task With_The_Guard_A_BaselineActivitySnapshots_Field_Cannot_Be_Rewritten_Through_An_Ordinary_DbContext(string propertyName)
    {
        var (tenantId, _, snapshotRowId, databaseName) = await SeedOneBaselineWithASnapshotAsync(withGuard: true);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using var tamperContext = CreateContext(databaseName, tenantProvider, withGuard: true);
        var snapshot = await tamperContext.Set<BaselineActivitySnapshot>().SingleAsync(s => s.Id == snapshotRowId);

        object tamperedValue = propertyName switch
        {
            nameof(BaselineActivitySnapshot.BudgetCost) => 999_999_999.00m,
            nameof(BaselineActivitySnapshot.DurationDays) => 999,
            nameof(BaselineActivitySnapshot.PlannedStart) => Now.AddYears(1),
            _ => throw new InvalidOperationException($"Unhandled property {propertyName}"),
        };
        tamperContext.Entry(snapshot).Property(propertyName).CurrentValue = tamperedValue;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => tamperContext.SaveChangesAsync());
        Assert.Contains(nameof(BaselineActivitySnapshot), ex.Message);
        Assert.Contains("append-only", ex.Message);
    }

    [Fact]
    public async Task With_The_Guard_Deleting_A_BaselineActivitySnapshot_Throws_And_The_Row_Survives()
    {
        var (tenantId, _, snapshotRowId, databaseName) = await SeedOneBaselineWithASnapshotAsync(withGuard: true);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using (var deleteContext = CreateContext(databaseName, tenantProvider, withGuard: true))
        {
            var snapshot = await deleteContext.Set<BaselineActivitySnapshot>().SingleAsync(s => s.Id == snapshotRowId);
            deleteContext.Remove(snapshot);

            await Assert.ThrowsAsync<InvalidOperationException>(() => deleteContext.SaveChangesAsync());
        }

        using var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: true);
        Assert.Equal(1, await verifyContext.Set<BaselineActivitySnapshot>().CountAsync());
    }

    [Fact]
    public async Task With_The_Guard_Adding_A_New_Baseline_Still_Succeeds_Append_Is_Still_Allowed()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var databaseName = Guid.NewGuid().ToString();

        using var context = CreateContext(databaseName, tenantProvider, withGuard: true);
        var baseline = Domain.Entities.Baseline.Capture(
            tenantId, Guid.NewGuid(), "BL-0", Now, Guid.NewGuid(), 485_000_000.00m, []);
        context.Baselines.Add(baseline);

        await context.SaveChangesAsync(); // must not throw - Added is not Modified/Deleted.

        Assert.Equal(1, await context.Baselines.CountAsync());
    }

    /// <summary>
    /// The other, equally important half for the <em>parent</em> row specifically: unlike
    /// <see cref="BaselineActivitySnapshot"/>, <see cref="Domain.Entities.Baseline"/> is deliberately
    /// NOT <see cref="IAppendOnly"/> (its own remarks explain why - <c>IsActive</c> must stay
    /// mutable for the project's whole life). This confirms the guard - wired for the same
    /// <see cref="CmPlusDbContext"/> - does not accidentally block that legitimate mutation, the
    /// same "must not break the happy path" proof every other guarded aggregate in this file
    /// carries (e.g. <c>RecordPayment</c> on a <c>Certified</c> <c>PaymentCertificate</c> above).
    /// </summary>
    [Fact]
    public async Task With_The_Guard_A_Baselines_IsActive_Can_Still_Be_Legitimately_Toggled()
    {
        var (tenantId, baselineId, _, databaseName) = await SeedOneBaselineWithASnapshotAsync(withGuard: true);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using (var activateContext = CreateContext(databaseName, tenantProvider, withGuard: true))
        {
            var baseline = await activateContext.Baselines.SingleAsync(b => b.Id == baselineId);
            baseline.Activate();

            await activateContext.SaveChangesAsync(); // must NOT throw - Baseline is not IAppendOnly.
        }

        var afterActivate = await LoadBaselineUntrackedAsync(databaseName, tenantProvider, baselineId);
        Assert.True(afterActivate.IsActive);

        using (var deactivateContext = CreateContext(databaseName, tenantProvider, withGuard: true))
        {
            var baseline = await deactivateContext.Baselines.SingleAsync(b => b.Id == baselineId);
            baseline.Deactivate();

            await deactivateContext.SaveChangesAsync(); // must NOT throw either.
        }

        var afterDeactivate = await LoadBaselineUntrackedAsync(databaseName, tenantProvider, baselineId);
        Assert.False(afterDeactivate.IsActive);
    }

    private static async Task<Domain.Entities.Baseline> LoadBaselineUntrackedAsync(
        string databaseName, FakeTenantProvider tenantProvider, Guid baselineId)
    {
        using var context = CreateContext(databaseName, tenantProvider, withGuard: true);
        return await context.Baselines.AsNoTracking().SingleAsync(b => b.Id == baselineId);
    }
}
