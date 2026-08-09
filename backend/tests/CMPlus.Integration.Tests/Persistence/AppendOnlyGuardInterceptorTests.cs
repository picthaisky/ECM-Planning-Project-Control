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
}
