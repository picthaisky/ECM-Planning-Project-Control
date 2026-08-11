using CMPlus.Application.Approval;
using CMPlus.Application.Features.VariationOrder.Commands.Approve;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Vo;

/// <summary>
/// Sprint-10 security review H-03: <c>Project</c> carried no optimistic-concurrency token, so two
/// concurrent <c>VariationOrder</c> final approvals on the SAME project could lose one BAC/
/// ContractValue move entirely - the second write silently overwrote the first's with no error at
/// all, and both VOs ended <c>Approved</c> with immutable <c>BacBefore</c>/<c>BacAfter</c> stamps that
/// agreed with neither the pre- nor the post-race <c>Project.BAC</c>. Reproduces the review's own
/// probe shape - two independent <see cref="CmPlusDbContext"/>s, the shape two simultaneous HTTP
/// requests naturally have (each request's own <c>DbContext</c> reads <c>Project</c> once, up front,
/// before either request's <c>SaveChanges</c> runs) - through the real
/// <see cref="ApproveVariationOrderCommandHandler"/>, not raw context pokes.
/// </summary>
public class VariationOrderConcurrencyIntegrationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-10T09:00:00+07:00");
    private static readonly DateTimeOffset EffectiveFrom = DateTimeOffset.Parse("2025-01-01T00:00:00+07:00");

    /// <summary>
    /// Both VOs resolve to a single PM-only step (well under the 500,000.00 band, and their combined
    /// ratio against a 485,000,000.00 baseline is nowhere near the 10.00% cumulative-VO-escalation
    /// threshold) purely to isolate H-03's concurrency mechanic from H-01's escalation guard - one
    /// <c>Approve</c> call finalizes each.
    /// </summary>
    [Fact]
    public async Task Two_Concurrent_Vo_Final_Approvals_On_The_Same_Project_The_Second_Gets_409_Never_A_Silent_Lost_Update()
    {
        var harness = new VariationOrderWorkflowHarness(now: Now);
        await harness.SeedDefaultApprovalPoliciesAsync(EffectiveFrom);
        var projectId = await harness.SeedProjectAsync(bac: 485_000_000.00m);
        var creatorId = Guid.NewGuid();

        var activityAId = await harness.SeedActivityAsync(projectId, budgetCost: 900_000_000.00m);
        var activityBId = await harness.SeedActivityAsync(projectId, budgetCost: 900_000_000.00m);
        var voAId = await harness.CreateDraftAsync(
            projectId, 300_000.00m, creatorId, [new VariationOrderScopeItemInput(activityAId, 300_000.00m)]);
        var voBId = await harness.CreateDraftAsync(
            projectId, 250_000.00m, creatorId, [new VariationOrderScopeItemInput(activityBId, 250_000.00m)]);
        Assert.True((await harness.SubmitAsync(voAId, creatorId, UserRole.QS)).IsSuccess);
        Assert.True((await harness.SubmitAsync(voBId, creatorId, UserRole.QS)).IsSuccess);

        // Two independent contexts - the shape two simultaneous HTTP requests have. Pre-warm BOTH
        // contexts' change trackers with the Project row BEFORE either approval commits: EF Core's
        // identity resolution means a LATER query for an already-tracked entity on the SAME context
        // returns the existing (stale-OriginalValues) instance rather than re-reading the database, so
        // this reproduces genuine interleaving without needing actual threads.
        var contextA = harness.CreateContext();
        var contextB = harness.CreateContext();
        try
        {
            _ = await contextA.Projects.SingleAsync(p => p.Id == projectId);
            _ = await contextB.Projects.SingleAsync(p => p.Id == projectId);

            var handlerA = BuildApproveHandler(harness, contextA);
            var handlerB = BuildApproveHandler(harness, contextB);

            harness.ActAs(Guid.NewGuid(), UserRole.PM);
            var resultA = await handlerA.Handle(new ApproveVariationOrderCommand(voAId, null), CancellationToken.None);
            Assert.True(resultA.IsSuccess, resultA.IsFailure ? resultA.Error : string.Empty); // first writer wins

            harness.ActAs(Guid.NewGuid(), UserRole.PM);
            var resultB = await handlerB.Handle(new ApproveVariationOrderCommand(voBId, null), CancellationToken.None);

            // H-03's fix: the SECOND writer gets a typed 409, never a silent lost update.
            Assert.True(resultB.IsFailure);
            Assert.Equal("VariationOrderConcurrencyConflict", resultB.Error);
        }
        finally
        {
            contextA.Dispose();
            contextB.Dispose();
        }

        // Only A's move landed so far - B's own attempt was rejected, not silently discarded.
        var projectAfterFirstRound = await harness.LoadProjectAsync(projectId);
        Assert.Equal(485_300_000.00m, projectAfterFirstRound.BAC);
        var voAAfterFirstRound = await harness.LoadAsync(voAId);
        Assert.Equal(VariationOrderStatus.Approved, voAAfterFirstRound.Status);
        Assert.Equal(485_000_000.00m, voAAfterFirstRound.BacBefore);
        Assert.Equal(485_300_000.00m, voAAfterFirstRound.BacAfter);
        var voBAfterFirstRound = await harness.LoadAsync(voBId);
        Assert.Equal(VariationOrderStatus.PendingApproval, voBAfterFirstRound.Status); // never advanced
        Assert.Null(voBAfterFirstRound.BacBefore); // no effects recorded for the rejected attempt

        // The remedy: the loser retries against a FRESH context, which re-reads the current (post-A)
        // Project row - both moves land, correctly, with no lost update.
        using var retryContext = harness.CreateContext();
        var retryHandler = BuildApproveHandler(harness, retryContext);
        harness.ActAs(Guid.NewGuid(), UserRole.PM);
        var retryResult = await retryHandler.Handle(new ApproveVariationOrderCommand(voBId, null), CancellationToken.None);
        Assert.True(retryResult.IsSuccess, retryResult.IsFailure ? retryResult.Error : string.Empty);

        var projectFinal = await harness.LoadProjectAsync(projectId);
        // BOTH moves landed: 485,000,000.00 + 300,000.00 + 250,000.00 = 485,550,000.00 - never
        // 485,300,000.00 (B lost) or 485,250,000.00 (A lost), the two ways H-03 could silently drop money.
        Assert.Equal(485_550_000.00m, projectFinal.BAC);
        var voBFinal = await harness.LoadAsync(voBId);
        Assert.Equal(VariationOrderStatus.Approved, voBFinal.Status);
        Assert.Equal(485_300_000.00m, voBFinal.BacBefore); // the retry correctly saw A's already-landed move
        Assert.Equal(485_550_000.00m, voBFinal.BacAfter);
    }

    private static ApproveVariationOrderCommandHandler BuildApproveHandler(VariationOrderWorkflowHarness harness, CmPlusDbContext context) =>
        new(
            new VariationOrderRepository(context),
            new ProjectRepository(context),
            new ApprovalPolicyReader(context),
            new ApprovalRoutingService(),
            new ApprovalActionRepository(context),
            new PaymentCertificateRepository(context),
            harness.TenantProvider,
            harness.CurrentUser,
            harness.Clock);
}
