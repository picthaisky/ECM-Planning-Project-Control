using CMPlus.Application.Abstractions;
using CMPlus.Application.Approval;
using CMPlus.Application.Features.VariationOrder;
using CMPlus.Application.Features.VariationOrder.Commands.Approve;
using CMPlus.Application.Features.VariationOrder.Commands.Reject;
using CMPlus.Application.Features.VariationOrder.Commands.ReturnForRevision;
using CMPlus.Application.Features.VariationOrder.Commands.Submit;
using CMPlus.Application.Features.VariationOrder.Commands.UpdateContent;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Interceptors;
using CMPlus.Integration.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Vo;

/// <summary>
/// S10-BE-01/02/03: end-to-end test harness against a real <see cref="CmPlusDbContext"/> (EF Core
/// InMemory, per the Docker outage) wired with the <b>real</b> production interceptors
/// (<see cref="AuditSaveChangesInterceptor"/>/<see cref="RowVersionSaveChangesInterceptor"/>/
/// <see cref="AppendOnlyGuardInterceptor"/>) and repositories/handlers - not fakes. Mirrors
/// <c>CMPlus.Integration.Tests.Approval.ApprovalWorkflowHarness</c>'s exact shape (S9 precedent):
/// every "step" method opens and disposes its own fresh <see cref="CmPlusDbContext"/> against the
/// same named InMemory database, so two "simultaneous" requests are genuinely two independent
/// contexts racing, not one shared tracked instance.
/// </summary>
internal sealed class MutableCurrentUserContext : ICurrentUserContext
{
    public Guid? UserId { get; set; }

    public UserRole Role { get; set; } = UserRole.QS;
}

internal sealed class MutableClock(DateTimeOffset now) : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

internal sealed class VariationOrderWorkflowHarness
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    public Guid TenantId { get; }

    public FakeTenantProvider TenantProvider { get; }

    public MutableCurrentUserContext CurrentUser { get; } = new();

    public MutableClock Clock { get; }

    public VariationOrderWorkflowHarness(Guid? tenantId = null, DateTimeOffset? now = null)
    {
        TenantId = tenantId ?? Guid.NewGuid();
        TenantProvider = new FakeTenantProvider(TenantId);
        Clock = new MutableClock(now ?? DateTimeOffset.Parse("2026-08-10T09:00:00+07:00"));
    }

    public CmPlusDbContext CreateContext()
    {
        var auditInterceptor = new AuditSaveChangesInterceptor(TenantProvider, CurrentUser, Clock);
        var rowVersionInterceptor = new RowVersionSaveChangesInterceptor();
        var appendOnlyInterceptor = new AppendOnlyGuardInterceptor();

        var options = new DbContextOptionsBuilder<CmPlusDbContext>()
            .UseInMemoryDatabase(_databaseName)
            .AddInterceptors(auditInterceptor, rowVersionInterceptor, appendOnlyInterceptor)
            .Options;

        return new CmPlusDbContext(options, TenantProvider);
    }

    public void ActAs(Guid userId, UserRole role)
    {
        CurrentUser.UserId = userId;
        CurrentUser.Role = role;
    }

    public async Task<Guid> SeedProjectAsync(decimal bac, decimal? originalContractValue = null, decimal? contractValue = null)
    {
        using var context = CreateContext();
        var project = Project.Create(
            TenantId, "Project", $"P-{Guid.NewGuid():N}", "Owner",
            Clock.UtcNow.AddYears(-1), Clock.UtcNow.AddYears(1), bac, Clock.UtcNow, contractValue: contractValue ?? bac);
        context.Projects.Add(project);
        await context.SaveChangesAsync();
        return project.Id;
    }

    public async Task<Guid> SeedActivityAsync(Guid projectId, decimal budgetCost)
    {
        using var context = CreateContext();
        var wbsNode = new WBSNode(TenantId, projectId, $"C-{Guid.NewGuid():N}", "Node", 100m);
        context.WBSNodes.Add(wbsNode);
        var activity = new Activity(
            TenantId, wbsNode.Id, $"A-{Guid.NewGuid():N}", "Activity", Clock.UtcNow, Clock.UtcNow.AddDays(30), 10, budgetCost);
        context.Activities.Add(activity);
        await context.SaveChangesAsync();
        return activity.Id;
    }

    /// <summary>Seeds the real <c>TH-Default-VO</c>/<c>TH-Default-IPC</c> tenant-wide policies via the
    /// production <c>ApprovalPolicySeeder</c> - <c>CumulativeVoEscalationPct</c> is <c>NULL</c>
    /// (ADR-0015), matching real tenant provisioning exactly.</summary>
    public async Task SeedDefaultApprovalPoliciesAsync(DateTimeOffset effectiveFrom)
    {
        using var context = CreateContext();
        await CMPlus.Infrastructure.Persistence.Seed.ApprovalPolicySeeder.SeedDefaultPoliciesAsync(
            context, TenantId, effectiveFrom, CancellationToken.None);
    }

    /// <summary>Seeds a project-scoped VO policy override (preferred by <c>ApprovalRoutingService.SelectPolicy</c>
    /// over the tenant-wide default) with a configured cumulative-VO-escalation threshold - the shape
    /// R4/V-4/V-5/V-6 all need and the tenant default deliberately does not carry.</summary>
    public async Task<Guid> SeedEscalationPolicyAsync(
        Guid projectId, DateTimeOffset effectiveFrom, decimal thresholdPct = 10.00m, UserRole escalationRole = UserRole.Executive)
    {
        using var context = CreateContext();
        var policy = ApprovalPolicy.CreateInitialVersion(
            TenantId, projectId, ApprovalDocumentType.VariationOrder, effectiveFrom,
            [
                new ApprovalPolicyRuleInput(1, 0.00m, 500_000.00m, UserRole.PM),
                new ApprovalPolicyRuleInput(1, 500_000.00m, 5_000_000.00m, UserRole.PM),
                new ApprovalPolicyRuleInput(2, 500_000.00m, 5_000_000.00m, UserRole.ProjectDirector),
                new ApprovalPolicyRuleInput(1, 5_000_000.00m, null, UserRole.PM),
                new ApprovalPolicyRuleInput(2, 5_000_000.00m, null, UserRole.ProjectDirector),
                new ApprovalPolicyRuleInput(3, 5_000_000.00m, null, UserRole.Executive),
            ],
            allowSelfApproval: false, cumulativeVoEscalationPct: thresholdPct, cumulativeVoEscalationRole: escalationRole);
        context.ApprovalPolicies.Add(policy);
        await context.SaveChangesAsync();
        return policy.Id;
    }

    /// <summary>Seeds a project-scoped policy override whose lowest <c>MinAmount</c> is 100,000.00 -
    /// R5's "TH-Gap-VO".</summary>
    public async Task SeedGapPolicyAsync(Guid projectId, DateTimeOffset effectiveFrom)
    {
        using var context = CreateContext();
        var policy = ApprovalPolicy.CreateInitialVersion(
            TenantId, projectId, ApprovalDocumentType.VariationOrder, effectiveFrom,
            [new ApprovalPolicyRuleInput(1, 100_000.00m, null, UserRole.PM)]);
        context.ApprovalPolicies.Add(policy);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// H-01 (docs/security/reviews/sprint-10.md): reproduces the ONE effect an ordinary
    /// <c>UpdateApprovalPolicyCommandHandler</c> edit has on the pinned policy an in-flight document
    /// still references - <c>current?.Deactivate(now)</c>, called unconditionally on every save
    /// (<c>UpdateApprovalPolicyCommandHandler.cs:45</c>) - without needing the full HTTP/policy-write
    /// surface. A real Admin editing a band, a quorum, or fixing a typo all take this exact same path.
    /// </summary>
    public async Task DeactivatePolicyAsync(Guid policyId)
    {
        using var context = CreateContext();
        var policy = await context.ApprovalPolicies.SingleAsync(p => p.Id == policyId);
        policy.Deactivate(Clock.UtcNow);
        await context.SaveChangesAsync();
    }

    public async Task<Guid> CreateDraftAsync(
        Guid projectId, decimal amount, Guid createdByUserId, IReadOnlyList<VariationOrderScopeItemInput>? scopeItems = null,
        UserRole createdByRole = UserRole.QS, string? voNumber = null)
    {
        ActAs(createdByUserId, createdByRole);
        using var context = CreateContext();
        var vo = new Domain.Entities.VariationOrder(
            TenantId, projectId, voNumber ?? $"VO-{Guid.NewGuid():N}", createdByUserId, amount, "Test VO", null, 0,
            scopeItems ?? [new VariationOrderScopeItemInput(Guid.NewGuid(), amount)]);
        context.VariationOrders.Add(vo);
        await context.SaveChangesAsync();
        return vo.Id;
    }

    public async Task<Domain.Entities.VariationOrder> LoadAsync(Guid variationOrderId)
    {
        using var context = CreateContext();
        return await context.VariationOrders.AsNoTracking()
            .Include(v => v.ApprovalSteps).Include(v => v.ScopeItems)
            .SingleAsync(v => v.Id == variationOrderId);
    }

    public async Task<Project> LoadProjectAsync(Guid projectId)
    {
        using var context = CreateContext();
        return await context.Projects.AsNoTracking().SingleAsync(p => p.Id == projectId);
    }

    public async Task<Activity> LoadActivityAsync(Guid activityId)
    {
        using var context = CreateContext();
        return await context.Activities.AsNoTracking().SingleAsync(a => a.Id == activityId);
    }

    public async Task<List<ApprovalAction>> LoadActionsAsync(Guid variationOrderId)
    {
        using var context = CreateContext();
        return await context.ApprovalActions.AsNoTracking()
            .Where(a => a.DocumentId == variationOrderId)
            .OrderBy(a => a.ActedAt)
            .ToListAsync();
    }

    public async Task<int> CountProjectFinanceLedgerRowsAsync(Guid projectId)
    {
        using var context = CreateContext();
        return await context.ProjectFinanceLedgers.AsNoTracking().Where(l => l.ProjectId == projectId).CountAsync();
    }

    public async Task<List<AuditLog>> LoadAuditLogsAsync(string entityName, Guid entityId)
    {
        using var context = CreateContext();
        return await context.AuditLogs.AsNoTracking()
            .Where(l => l.EntityName == entityName && l.EntityId == entityId)
            .OrderBy(l => l.Timestamp)
            .ToListAsync();
    }

    public async Task<Result<VariationOrderDto>> SubmitAsync(Guid variationOrderId, Guid actorId, UserRole actorRole)
    {
        ActAs(actorId, actorRole);
        using var context = CreateContext();
        var handler = new SubmitVariationOrderCommandHandler(
            new VariationOrderRepository(context),
            new ProjectRepository(context),
            new ApprovalPolicyReader(context),
            new ApprovalRoutingService(),
            new ApprovalActionRepository(context),
            TenantProvider,
            CurrentUser,
            Clock);

        return await handler.Handle(new SubmitVariationOrderCommand(variationOrderId), CancellationToken.None);
    }

    public async Task<Result<VariationOrderDto>> ApproveAsync(Guid variationOrderId, Guid actorId, UserRole actorRole, string? comment = null)
    {
        ActAs(actorId, actorRole);
        using var context = CreateContext();
        var handler = new ApproveVariationOrderCommandHandler(
            new VariationOrderRepository(context),
            new ProjectRepository(context),
            new ApprovalPolicyReader(context),
            new ApprovalRoutingService(),
            new ApprovalActionRepository(context),
            new PaymentCertificateRepository(context),
            TenantProvider,
            CurrentUser,
            Clock);

        return await handler.Handle(new ApproveVariationOrderCommand(variationOrderId, comment), CancellationToken.None);
    }

    public async Task<Result<VariationOrderDto>> RejectAsync(Guid variationOrderId, Guid actorId, UserRole actorRole, string comment)
    {
        ActAs(actorId, actorRole);
        using var context = CreateContext();
        var handler = new RejectVariationOrderCommandHandler(
            new VariationOrderRepository(context),
            new ApprovalActionRepository(context),
            TenantProvider,
            CurrentUser,
            Clock);

        return await handler.Handle(new RejectVariationOrderCommand(variationOrderId, comment), CancellationToken.None);
    }

    public async Task<Result<VariationOrderDto>> ReturnForRevisionAsync(Guid variationOrderId, Guid actorId, UserRole actorRole, string comment)
    {
        ActAs(actorId, actorRole);
        using var context = CreateContext();
        var handler = new ReturnVariationOrderForRevisionCommandHandler(
            new VariationOrderRepository(context),
            new ApprovalActionRepository(context),
            TenantProvider,
            CurrentUser,
            Clock);

        return await handler.Handle(new ReturnVariationOrderForRevisionCommand(variationOrderId, comment), CancellationToken.None);
    }

    public async Task<Result<VariationOrderDto>> UpdateContentAsync(
        Guid variationOrderId, decimal amount, int timeImpactDays, IReadOnlyList<VariationOrderScopeItemInput> scopeItems)
    {
        using var context = CreateContext();
        var handler = new UpdateVariationOrderContentCommandHandler(new VariationOrderRepository(context));

        return await handler.Handle(
            new UpdateVariationOrderContentCommand(variationOrderId, "Re-priced", null, amount, timeImpactDays, scopeItems),
            CancellationToken.None);
    }
}
