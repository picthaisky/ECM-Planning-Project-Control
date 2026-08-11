using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Interceptors;
using CMPlus.Integration.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Eot;

/// <summary>
/// S11-QA-01: independent verification of a gap found while reading the codebase - unlike
/// <c>DailyWeatherLog</c>/<c>CpmRun</c> (both proven structurally immutable through a raw
/// <see cref="CmPlusDbContext"/> in <c>AppendOnlyGuardInterceptorTests</c>), <see cref="EotEvaluation"/>
/// itself - the record §2.1/fixture W-14 calls the whole zero-side-effect boundary out by name - had
/// NO test anywhere proving <see cref="Infrastructure.Persistence.Interceptors.AppendOnlyGuardInterceptor"/>
/// actually applies to it. <c>Handle_W14_...</c> (<c>EvaluateEotCommandHandlerIntegrationTests</c>)
/// proves the EVALUATOR never writes to OTHER tables; this file proves the evaluation's OWN row,
/// once written, cannot itself be silently rewritten later through an ordinary DbContext - the same
/// M-01 pattern this codebase already applies everywhere else <see cref="IAppendOnly"/> is used.
/// Mirrors <c>AppendOnlyGuardInterceptorTests</c>'s own structure exactly (baseline-without-guard,
/// then with-guard-blocks-edit/delete, then with-guard-still-allows-append) rather than inventing a
/// new pattern for one more aggregate.
/// </summary>
public class EotEvaluationAppendOnlyGuardTests
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

    /// <summary>W-16a's own vacuous-but-valid shape (no runs/sources/drivers) - the guard must not
    /// depend on the evaluation having children to protect the parent row.</summary>
    private static EotEvaluation BuildMinimalEvaluation(Guid tenantId, Guid projectId, Guid evaluatedByUserId) =>
        EotEvaluation.Capture(
            tenantId, projectId,
            windowStart: DateTimeOffset.Parse("2026-07-01T00:00:00+07:00"),
            windowEnd: DateTimeOffset.Parse("2026-07-31T00:00:00+07:00"),
            evaluatedAt: DateTimeOffset.Parse("2026-08-01T00:00:00+07:00"),
            evaluatedByUserId,
            EotCriticalityBasis.Contemporaneous,
            EotConfidence.Substantiated,
            asScheduledDurationDays: 0,
            impactedDurationDays: 0,
            eotEligibleDays: 0,
            countableStoppageDayCount: 0,
            serialChainAbsorbedDayCount: 0,
            distinctCountableDateCount: 0,
            unattributedStoppageDayCount: 0,
            policySnapshotJson: "{}",
            latestNoticeDate: null,
            noticeWindowExpired: null,
            runs: [],
            sources: [],
            drivers: []);

    private static async Task<(Guid TenantId, Guid EvaluationId, string DatabaseName)> SeedOneEvaluationAsync(bool withGuard)
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var databaseName = Guid.NewGuid().ToString();

        using var context = CreateContext(databaseName, tenantProvider, withGuard);
        var evaluation = BuildMinimalEvaluation(tenantId, Guid.NewGuid(), Guid.NewGuid());
        context.EotEvaluations.Add(evaluation);
        await context.SaveChangesAsync();

        return (tenantId, evaluation.Id, databaseName);
    }

    /// <summary>Reproduces the M-01 shape exactly (review probe 7, reproduced for this aggregate,
    /// same as the <c>DailyWeatherLog</c>/<c>CpmRun</c> precedents): without the guard wired in, a
    /// supposedly-immutable evaluation record can still be silently rewritten - the exact defect this
    /// task's brief warns "the entity has no mutator" is not, by itself, sufficient to prevent
    /// (Sprint 9's M-01).</summary>
    [Fact]
    public async Task Without_The_Guard_An_EotEvaluations_EotEligibleDays_Can_Still_Be_Rewritten_Through_An_Ordinary_DbContext()
    {
        var (tenantId, evaluationId, databaseName) = await SeedOneEvaluationAsync(withGuard: false);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using (var tamperContext = CreateContext(databaseName, tenantProvider, withGuard: false))
        {
            var evaluation = await tamperContext.EotEvaluations.SingleAsync(e => e.Id == evaluationId);
            tamperContext.Entry(evaluation).Property(nameof(EotEvaluation.EotEligibleDays)).CurrentValue = 999;
            await tamperContext.SaveChangesAsync(); // succeeds today without the guard - the baseline the fix closes.
        }

        using var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: false);
        var tampered = await verifyContext.EotEvaluations.AsNoTracking().SingleAsync(e => e.Id == evaluationId);
        Assert.Equal(999, tampered.EotEligibleDays);
    }

    [Fact]
    public async Task With_The_Guard_An_EotEvaluations_EotEligibleDays_Cannot_Be_Rewritten_Through_An_Ordinary_DbContext()
    {
        var (tenantId, evaluationId, databaseName) = await SeedOneEvaluationAsync(withGuard: true);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using (var tamperContext = CreateContext(databaseName, tenantProvider, withGuard: true))
        {
            var evaluation = await tamperContext.EotEvaluations.SingleAsync(e => e.Id == evaluationId);
            tamperContext.Entry(evaluation).Property(nameof(EotEvaluation.EotEligibleDays)).CurrentValue = 999;

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => tamperContext.SaveChangesAsync());
            Assert.Contains(nameof(EotEvaluation), ex.Message);
            Assert.Contains("append-only", ex.Message);
        }

        using var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: true);
        var untouched = await verifyContext.EotEvaluations.AsNoTracking().SingleAsync(e => e.Id == evaluationId);
        Assert.Equal(0, untouched.EotEligibleDays); // still the original value - a rewrite attempt must leave no trace.
    }

    [Fact]
    public async Task With_The_Guard_Deleting_An_EotEvaluation_Throws_And_The_Row_Survives()
    {
        var (tenantId, evaluationId, databaseName) = await SeedOneEvaluationAsync(withGuard: true);
        var tenantProvider = new FakeTenantProvider(tenantId);

        using (var deleteContext = CreateContext(databaseName, tenantProvider, withGuard: true))
        {
            var evaluation = await deleteContext.EotEvaluations.SingleAsync(e => e.Id == evaluationId);
            deleteContext.EotEvaluations.Remove(evaluation);

            await Assert.ThrowsAsync<InvalidOperationException>(() => deleteContext.SaveChangesAsync());
        }

        using var verifyContext = CreateContext(databaseName, tenantProvider, withGuard: true);
        Assert.Equal(1, await verifyContext.EotEvaluations.CountAsync());
    }

    /// <summary>The other, equally important half: the guard must not block the one legitimate write
    /// path (a brand-new evaluation via <c>EvaluateEotCommandHandler</c>) - mirrors every sibling
    /// aggregate's identical "append still works" proof.</summary>
    [Fact]
    public async Task With_The_Guard_Adding_A_New_EotEvaluation_Still_Succeeds_Append_Is_Still_Allowed()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var databaseName = Guid.NewGuid().ToString();

        using var context = CreateContext(databaseName, tenantProvider, withGuard: true);
        var evaluation = BuildMinimalEvaluation(tenantId, Guid.NewGuid(), Guid.NewGuid());
        context.EotEvaluations.Add(evaluation);

        await context.SaveChangesAsync(); // must not throw - Added is not Modified/Deleted.

        Assert.Equal(1, await context.EotEvaluations.CountAsync());
    }
}
