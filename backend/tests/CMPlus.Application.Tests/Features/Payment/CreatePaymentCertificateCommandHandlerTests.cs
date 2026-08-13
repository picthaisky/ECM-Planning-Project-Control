using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Payment;
using CMPlus.Application.Features.Payment.Commands.CreatePaymentCertificate;
using CMPlus.Application.Services.Payment;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Payment;

/// <summary>
/// S9-BE-05 create. Money-critical: these assert exact hand-computed money outputs (not just "a
/// certificate came back") and the two auto-derivations the handler owns - the per-milestone previous
/// cumulative and the Certified/Paid-only filter that decides which priors count.
/// </summary>
public class CreatePaymentCertificateCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private sealed class FakeProjectRepo(Project? project) : IProjectRepository
    {
        public Task<Project?> FindAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(project is not null && project.Id == projectId ? project : null);

        public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class FakeLedgerReader(decimal retentionHeld = 0m, decimal advanceRecovered = 0m) : IProjectFinanceLedgerReader
    {
        public Task<decimal> GetRetentionHeldAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(retentionHeld);

        public Task<decimal> GetAdvanceRecoveredAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(advanceRecovered);

        public Task<decimal> GetTotalDisbursedAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(0m);
    }

    /// <summary>A project configured for the clean, hand-verifiable fixture: 5% retention (uncapped),
    /// 0% advance (so advance recovery is 0 and Net = Gross - Retention exactly), pro-rata method.</summary>
    private static Project ConfiguredProject(decimal? retentionRate = 5.00m, decimal? advanceRate = 0.00m) =>
        Project.Create(
            TenantId, "P", $"P-{Guid.NewGuid():N}", "Owner",
            DateTimeOffset.UtcNow.AddYears(-1), DateTimeOffset.UtcNow.AddYears(1),
            bac: 100_000_000.00m, DateTimeOffset.UtcNow,
            retentionRate: retentionRate, advanceRate: advanceRate, contractValue: 100_000_000.00m);

    private static (CreatePaymentCertificateCommandHandler Handler, FakePaymentCertificateRepository Certs) CreateHandler(
        Project? project, Guid? actor, params PaymentCertificate[] seededPriors)
    {
        var certs = new FakePaymentCertificateRepository();
        foreach (var prior in seededPriors)
        {
            certs.Seed(prior);
        }

        var handler = new CreatePaymentCertificateCommandHandler(
            new FakeProjectRepo(project),
            certs,
            new FakeLedgerReader(),
            new FakeTenantProviderForPayment(TenantId),
            new FakeCurrentUserContextForPayment(actor, UserRole.QS));
        return (handler, certs);
    }

    private static CreatePaymentCertificateCommand Command(Guid projectId, decimal thisCumulativePct, int milestoneNo = 1) =>
        new(projectId, milestoneNo, "IPC 1", MilestoneValue: 20_000_000.00m, thisCumulativePct, ClaimPct: null,
            ActualProgressPct: null, ManualAdvanceRecoveryAmount: null);

    /// <summary>Builds a prior certificate already at a certified/committed state with a known
    /// ApprovePct, so the handler's "which priors count" filter can be exercised.</summary>
    private static PaymentCertificate SeededCertificate(
        Guid projectId, int milestoneNo, decimal approvePct, decimal gross, PaymentCertificateStatus status)
    {
        var cert = new PaymentCertificate(TenantId, projectId, milestoneNo, "prior", 20_000_000.00m, 0m, Guid.NewGuid());
        cert.SetPeriodClaim(approvePct, null, null, gross, retentionAmount: 0m, advanceRecoveryAmount: 0m, netPayment: gross);
        ForceStatus(cert, status);
        return cert;
    }

    // The only way to a committed status from a fresh Draft without driving the whole approval chain
    // in a unit test; reflection (via the non-public setter) is confined to this test helper.
    private static void ForceStatus(PaymentCertificate cert, PaymentCertificateStatus status) =>
        typeof(PaymentCertificate).GetProperty(nameof(PaymentCertificate.Status))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(cert, [status]);

    [Fact]
    public async Task Creates_A_Draft_Certificate_With_Exact_Money_From_The_Project_Config_And_Zero_Prior()
    {
        var project = ConfiguredProject();
        var (handler, certs) = CreateHandler(project, actor: Guid.NewGuid());

        var result = await handler.Handle(Command(project.Id, thisCumulativePct: 30.00m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dto = result.Value;
        Assert.Equal(PaymentCertificateStatus.Draft, dto.Status);
        Assert.Equal(0m, dto.PreviousCumulativeApprovePct);      // no priors
        Assert.Equal(30.00m, dto.ApprovePct);
        Assert.Equal(6_000_000.00m, dto.GrossCertifiedAmount);   // 20M * 30%
        Assert.Equal(300_000.00m, dto.RetentionAmount);          // 5% * 6M, uncapped
        Assert.Equal(5_700_000.00m, dto.NetPayment);             // 6M - 300k - 0 advance
        Assert.NotNull(certs.AddedCertificate);
        Assert.Equal(1, certs.SaveCallCount);
    }

    [Fact]
    public async Task Auto_Derives_Previous_Cumulative_From_The_Same_Milestones_Certified_Prior()
    {
        var project = ConfiguredProject();
        var certifiedPrior = SeededCertificate(project.Id, milestoneNo: 1, approvePct: 30.00m, gross: 6_000_000.00m, PaymentCertificateStatus.Certified);
        var (handler, _) = CreateHandler(project, actor: Guid.NewGuid(), certifiedPrior);

        var result = await handler.Handle(Command(project.Id, thisCumulativePct: 50.00m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(30.00m, result.Value.PreviousCumulativeApprovePct);   // derived, not client-supplied
        Assert.Equal(4_000_000.00m, result.Value.GrossCertifiedAmount);    // 20M*(50-30)% = period gross only
        Assert.Equal(200_000.00m, result.Value.RetentionAmount);           // 5% * 4M
        Assert.Equal(3_800_000.00m, result.Value.NetPayment);
    }

    [Fact]
    public async Task A_Rejected_Or_Draft_Prior_Does_Not_Count_Toward_The_Cumulative_Floor()
    {
        // The money guard: an uncommitted/rejected claim at a HIGH pct must not raise the floor and
        // block a legitimate lower certification. Only Certified/Paid priors count.
        var project = ConfiguredProject();
        var rejectedHighPrior = SeededCertificate(project.Id, milestoneNo: 1, approvePct: 80.00m, gross: 16_000_000.00m, PaymentCertificateStatus.Rejected);
        var draftHighPrior = SeededCertificate(project.Id, milestoneNo: 1, approvePct: 90.00m, gross: 18_000_000.00m, PaymentCertificateStatus.Draft);
        var (handler, _) = CreateHandler(project, actor: Guid.NewGuid(), rejectedHighPrior, draftHighPrior);

        var result = await handler.Handle(Command(project.Id, thisCumulativePct: 50.00m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value.PreviousCumulativeApprovePct);       // rejected 80% / draft 90% ignored
        Assert.Equal(10_000_000.00m, result.Value.GrossCertifiedAmount);   // 20M * 50%, not blocked
    }

    [Fact]
    public async Task Blocks_When_The_Derived_Previous_Cumulative_Makes_This_Period_Non_Monotonic()
    {
        var project = ConfiguredProject();
        var certifiedPrior = SeededCertificate(project.Id, milestoneNo: 1, approvePct: 60.00m, gross: 12_000_000.00m, PaymentCertificateStatus.Certified);
        var (handler, _) = CreateHandler(project, actor: Guid.NewGuid(), certifiedPrior);

        // 50% < the certified 60% floor.
        var result = await handler.Handle(Command(project.Id, thisCumulativePct: 50.00m), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrorCodes.ApprovePctNotMonotonic, result.Error);
    }

    [Fact]
    public async Task Fails_Closed_When_The_Project_Has_No_Retention_Rate_Configured()
    {
        var project = ConfiguredProject(retentionRate: null);
        var (handler, certs) = CreateHandler(project, actor: Guid.NewGuid());

        var result = await handler.Handle(Command(project.Id, thisCumulativePct: 30.00m), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrorCodes.RetentionRateNotConfigured, result.Error);
        Assert.Null(certs.AddedCertificate);   // nothing persisted
    }

    [Fact]
    public async Task Fails_With_ActorRequired_When_There_Is_No_Authenticated_User()
    {
        var project = ConfiguredProject();
        var (handler, _) = CreateHandler(project, actor: null);

        var result = await handler.Handle(Command(project.Id, thisCumulativePct: 30.00m), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentApprovalErrorCodes.ActorRequired, result.Error);
    }

    [Fact]
    public async Task Fails_With_ProjectNotFound_For_An_Unknown_Or_Cross_Tenant_Project()
    {
        var (handler, _) = CreateHandler(project: null, actor: Guid.NewGuid());

        var result = await handler.Handle(Command(Guid.NewGuid(), thisCumulativePct: 30.00m), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentApprovalErrorCodes.ProjectNotFound, result.Error);
    }
}
