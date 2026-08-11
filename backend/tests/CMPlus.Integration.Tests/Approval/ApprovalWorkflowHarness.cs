using CMPlus.Application.Abstractions;
using CMPlus.Application.Approval;
using CMPlus.Application.Features.Approval.Commands.UpdateApprovalPolicy;
using CMPlus.Application.Features.Payment;
using CMPlus.Application.Features.Payment.Commands.Approve;
using CMPlus.Application.Features.Payment.Commands.RecordPayment;
using CMPlus.Application.Features.Payment.Commands.Reject;
using CMPlus.Application.Features.Payment.Commands.ReturnForRevision;
using CMPlus.Application.Features.Payment.Commands.Submit;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Interceptors;
using CMPlus.Integration.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Approval;

internal sealed class MutableCurrentUserContext : ICurrentUserContext
{
    public Guid? UserId { get; set; }

    public UserRole Role { get; set; } = UserRole.QS;
}

internal sealed class MutableClock(DateTimeOffset now) : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

/// <summary>
/// S9-BE-05/06: end-to-end test harness against a real <see cref="CmPlusDbContext"/> (EF Core
/// InMemory, per the Docker outage) wired with the <b>real</b>
/// <see cref="AuditSaveChangesInterceptor"/>/<see cref="RowVersionSaveChangesInterceptor"/> and the
/// <b>real</b> production repositories/handlers this task built - not fakes. Every "step" method
/// opens and disposes its own fresh <see cref="CmPlusDbContext"/> against the same named InMemory
/// database, mirroring one scoped-per-HTTP-request unit of work exactly the way ASP.NET Core DI
/// would (the same discipline <c>PaymentCertificateConcurrencyTests</c>/
/// <c>UpdateProjectCommandHandlerIntegrationTests</c> already established), so two "simultaneous"
/// requests are genuinely two independent contexts racing, not one shared tracked instance.
/// </summary>
internal sealed class ApprovalWorkflowHarness
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    public Guid TenantId { get; }

    public FakeTenantProvider TenantProvider { get; }

    public MutableCurrentUserContext CurrentUser { get; } = new();

    public MutableClock Clock { get; }

    public ApprovalWorkflowHarness(Guid? tenantId = null, DateTimeOffset? now = null)
    {
        TenantId = tenantId ?? Guid.NewGuid();
        TenantProvider = new FakeTenantProvider(TenantId);
        Clock = new MutableClock(now ?? DateTimeOffset.Parse("2026-08-09T09:00:00+07:00"));
    }

    public CmPlusDbContext CreateContext()
    {
        var auditInterceptor = new AuditSaveChangesInterceptor(TenantProvider, CurrentUser, Clock);
        var rowVersionInterceptor = new RowVersionSaveChangesInterceptor();
        // M-01 (sprint-09.md): matches the real composition root's interceptor set so this harness
        // keeps exercising production wiring, not a stripped-down substitute.
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

    public async Task<Guid> SeedDraftCertificateAsync(
        Guid projectId, decimal grossCertifiedAmount, Guid createdByUserId, UserRole createdByRole = UserRole.QS,
        int milestoneNo = 1, decimal retentionAmount = 0m, decimal advanceRecoveryAmount = 0m)
    {
        ActAs(createdByUserId, createdByRole);
        using var context = CreateContext();

        var certificate = new PaymentCertificate(
            TenantId, projectId, milestoneNo, $"IPC {milestoneNo}", grossCertifiedAmount, previousCumulativeApprovePct: 0m, createdByUserId);
        certificate.SetPeriodClaim(
            100m, null, null, grossCertifiedAmount, retentionAmount, advanceRecoveryAmount,
            grossCertifiedAmount - retentionAmount - advanceRecoveryAmount);

        context.PaymentCertificates.Add(certificate);
        await context.SaveChangesAsync();
        return certificate.Id;
    }

    /// <summary>Seeds the real <c>TH-Default-VO</c>/<c>TH-Default-IPC</c> policies
    /// (approval-workflow.md §8) via the production <c>ApprovalPolicySeeder</c> - the fixtures under
    /// test route against the exact same tenant-provisioning shape a real tenant gets, not a
    /// hand-rolled substitute.</summary>
    public async Task SeedDefaultApprovalPoliciesAsync(DateTimeOffset effectiveFrom)
    {
        using var context = CreateContext();
        await CMPlus.Infrastructure.Persistence.Seed.ApprovalPolicySeeder.SeedDefaultPoliciesAsync(
            context, TenantId, effectiveFrom, CancellationToken.None);
    }

    public async Task<PaymentCertificate> LoadCertificateAsync(Guid certificateId)
    {
        using var context = CreateContext();
        return await context.PaymentCertificates.AsNoTracking().SingleAsync(c => c.Id == certificateId);
    }

    /// <summary>Same as <see cref="LoadCertificateAsync"/> but also loads
    /// <see cref="PaymentCertificate.ApprovalSteps"/> - security review sprint-09.md H-01 fix proof:
    /// callers that need to inspect the persisted chain snapshot itself (not just the scalar
    /// CurrentStepNo/TotalSteps fields) from a genuinely fresh <see cref="CmPlusDbContext"/>, the same
    /// way <see cref="Infrastructure.Persistence.PaymentCertificateRepository.FindAsync"/> does.</summary>
    public async Task<PaymentCertificate> LoadCertificateWithStepsAsync(Guid certificateId)
    {
        using var context = CreateContext();
        return await context.PaymentCertificates.AsNoTracking()
            .Include(c => c.ApprovalSteps)
            .SingleAsync(c => c.Id == certificateId);
    }

    public async Task<List<ApprovalAction>> LoadActionsAsync(Guid certificateId)
    {
        using var context = CreateContext();
        return await context.ApprovalActions.AsNoTracking()
            .Where(a => a.DocumentId == certificateId)
            .OrderBy(a => a.ActedAt)
            .ToListAsync();
    }

    public async Task<List<AuditLog>> LoadAuditLogsAsync(string entityName, Guid entityId)
    {
        using var context = CreateContext();
        return await context.AuditLogs.AsNoTracking()
            .Where(l => l.EntityName == entityName && l.EntityId == entityId)
            .OrderBy(l => l.Timestamp)
            .ToListAsync();
    }

    public async Task<Result<PaymentCertificateDto>> SubmitAsync(Guid certificateId, Guid actorId, UserRole actorRole)
    {
        ActAs(actorId, actorRole);
        using var context = CreateContext();
        var handler = new SubmitPaymentCertificateCommandHandler(
            new PaymentCertificateRepository(context),
            new ApprovalPolicyReader(context),
            new ApprovalRoutingService(),
            new ApprovalActionRepository(context),
            TenantProvider,
            CurrentUser,
            Clock);

        return await handler.Handle(new SubmitPaymentCertificateCommand(certificateId), CancellationToken.None);
    }

    public async Task<Result<PaymentCertificateDto>> ApproveAsync(Guid certificateId, Guid actorId, UserRole actorRole, string? comment = null)
    {
        ActAs(actorId, actorRole);
        using var context = CreateContext();
        var handler = new ApprovePaymentCertificateCommandHandler(
            new PaymentCertificateRepository(context),
            new ApprovalActionRepository(context),
            TenantProvider,
            CurrentUser,
            Clock);

        return await handler.Handle(new ApprovePaymentCertificateCommand(certificateId, comment), CancellationToken.None);
    }

    public async Task<Result<PaymentCertificateDto>> ReturnForRevisionAsync(Guid certificateId, Guid actorId, UserRole actorRole, string comment)
    {
        ActAs(actorId, actorRole);
        using var context = CreateContext();
        var handler = new ReturnPaymentCertificateForRevisionCommandHandler(
            new PaymentCertificateRepository(context),
            new ApprovalActionRepository(context),
            TenantProvider,
            CurrentUser,
            Clock);

        return await handler.Handle(new ReturnPaymentCertificateForRevisionCommand(certificateId, comment), CancellationToken.None);
    }

    public async Task<Result<PaymentCertificateDto>> RejectAsync(Guid certificateId, Guid actorId, UserRole actorRole, string comment)
    {
        ActAs(actorId, actorRole);
        using var context = CreateContext();
        var handler = new RejectPaymentCertificateCommandHandler(
            new PaymentCertificateRepository(context),
            new ApprovalActionRepository(context),
            TenantProvider,
            CurrentUser,
            Clock);

        return await handler.Handle(new RejectPaymentCertificateCommand(certificateId, comment), CancellationToken.None);
    }

    public async Task<Result<PaymentCertificateDto>> RecordPaymentAsync(Guid certificateId, Guid actorId, string reference, DateTimeOffset paidAt)
    {
        ActAs(actorId, UserRole.QS);
        using var context = CreateContext();
        var handler = new RecordPaymentForPaymentCertificateCommandHandler(
            new PaymentCertificateRepository(context),
            new ApprovalActionRepository(context),
            TenantProvider,
            CurrentUser,
            Clock);

        return await handler.Handle(new RecordPaymentForPaymentCertificateCommand(certificateId, reference, paidAt), CancellationToken.None);
    }

    public async Task<Result<CMPlus.Application.Features.Approval.Queries.GetApprovalPolicy.ApprovalPolicyDto>> UpdatePolicyAsync(
        ApprovalDocumentType documentType,
        bool allowSelfApproval,
        decimal? cumulativeVoEscalationPct,
        UserRole? cumulativeVoEscalationRole,
        IReadOnlyList<ApprovalPolicyRuleInput> rules)
    {
        using var context = CreateContext();
        var handler = new UpdateApprovalPolicyCommandHandler(
            new ApprovalPolicyRepository(context), TenantProvider, Clock);

        return await handler.Handle(
            new UpdateApprovalPolicyCommand(documentType, allowSelfApproval, cumulativeVoEscalationPct, cumulativeVoEscalationRole, rules),
            CancellationToken.None);
    }
}
