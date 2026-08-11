using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Persistence;

/// <summary>
/// S9-BE-01 DoD: "ใช้ RowVersion → ผู้อนุมัติพร้อมกัน 2 คน คนที่สองได้ 409" - two simultaneous
/// approvers, the second gets 409, never a double-advance. Proven here structurally against a real
/// <see cref="Microsoft.EntityFrameworkCore.DbContext"/> (EF Core InMemory, per the Docker outage):
/// <see cref="PaymentCertificate.RowVersion"/> is configured <c>IsRowVersion()</c>
/// (<c>PaymentCertificateConfiguration</c>), so EF Core itself - not application code - detects the
/// conflict and throws <see cref="DbUpdateConcurrencyException"/> on the second writer's
/// <c>SaveChangesAsync</c>. Translating that exception into an HTTP 409 ProblemDetails response is
/// the (not-yet-built) S9-BE-05 command handler's job - out of scope here, exactly as this sprint's
/// task instructions require.
///
/// <para>Unlike the rest of this folder's tests, these build their own <see cref="CmPlusDbContext"/>
/// instances directly (rather than through the shared <c>TestDbContextFactory</c>, which registers no
/// interceptors) so <see cref="RowVersionSaveChangesInterceptor"/> is wired in - the same
/// "construct a real context with the real interceptors" pattern
/// <c>RecalculateCpmCommandHandlerIntegrationTests</c> already uses for its own (SQL-Server-only)
/// concurrency-adjacent needs. See that interceptor's remarks for why this is needed for InMemory
/// specifically and why it is harmless against real SQL Server.</para>
/// </summary>
public class PaymentCertificateConcurrencyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-09T10:00:00+07:00");

    private static CmPlusDbContext CreateContext(string databaseName, FakeTenantProvider tenantProvider)
    {
        var options = new DbContextOptionsBuilder<CmPlusDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(new RowVersionSaveChangesInterceptor())
            .Options;

        return new CmPlusDbContext(options, tenantProvider);
    }

    [Fact]
    public async Task Two_Concurrent_Approvers_The_Second_SaveChanges_Throws_DbUpdateConcurrencyException()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var databaseName = Guid.NewGuid().ToString();
        var projectId = Guid.NewGuid();
        Guid certificateId;

        // Seed a certificate already at PendingApproval, single remaining step, so a single Approve
        // call from either "approver" would certify it.
        using (var seedContext = CreateContext(databaseName, tenantProvider))
        {
            var certificate = new PaymentCertificate(tenantId, projectId, 1, "IPC 1", 21_600_000.00m, 0m, Guid.NewGuid());
            certificate.SetPeriodClaim(100m, null, null, 21_600_000.00m, 1_080_000.00m, 2_160_000.00m, 18_360_000.00m);
            certificate.Submit([new PaymentCertificateApprovalStepInput(1, UserRole.QS, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), Now);
            seedContext.PaymentCertificates.Add(certificate);
            await seedContext.SaveChangesAsync();
            certificateId = certificate.Id;
        }

        // Two independent DbContexts both load the SAME row (same RowVersion) before either writes -
        // exactly what "two simultaneous approvers" means in practice.
        using var contextForApproverOne = CreateContext(databaseName, tenantProvider);
        using var contextForApproverTwo = CreateContext(databaseName, tenantProvider);

        var certificateSeenByApproverOne = await contextForApproverOne.PaymentCertificates.SingleAsync(c => c.Id == certificateId);
        var certificateSeenByApproverTwo = await contextForApproverTwo.PaymentCertificates.SingleAsync(c => c.Id == certificateId);

        certificateSeenByApproverOne.Approve(Guid.NewGuid(), UserRole.QS, UserRole.QS, allowSelfApproval: false, Now);
        certificateSeenByApproverTwo.Approve(Guid.NewGuid(), UserRole.QS, UserRole.QS, allowSelfApproval: false, Now);

        // Approver one wins - saves cleanly and the certificate is Certified exactly once.
        await contextForApproverOne.SaveChangesAsync();

        // Approver two's SaveChanges detects the stale RowVersion and throws - never a silent
        // double-advance through the chain.
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => contextForApproverTwo.SaveChangesAsync());

        using var verifyContext = CreateContext(databaseName, tenantProvider);
        var persisted = await verifyContext.PaymentCertificates.AsNoTracking().SingleAsync(c => c.Id == certificateId);
        Assert.Equal(PaymentCertificateStatus.Certified, persisted.Status);
    }

    [Fact]
    public async Task RowVersion_Changes_On_Every_Persisted_Mutation()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new FakeTenantProvider(tenantId);
        var databaseName = Guid.NewGuid().ToString();
        var projectId = Guid.NewGuid();
        Guid certificateId;
        byte[] rowVersionAfterInsert;

        using (var context = CreateContext(databaseName, tenantProvider))
        {
            var certificate = new PaymentCertificate(tenantId, projectId, 1, "IPC 1", 1_000_000.00m, 0m, Guid.NewGuid());
            context.PaymentCertificates.Add(certificate);
            await context.SaveChangesAsync();
            certificateId = certificate.Id;
            rowVersionAfterInsert = certificate.RowVersion;
        }

        Assert.NotEmpty(rowVersionAfterInsert);

        using var updateContext = CreateContext(databaseName, tenantProvider);
        var reloaded = await updateContext.PaymentCertificates.SingleAsync(c => c.Id == certificateId);
        reloaded.SetPeriodClaim(50m, null, null, 500_000.00m, 25_000.00m, 50_000.00m, 425_000.00m);
        await updateContext.SaveChangesAsync();

        Assert.NotEqual(rowVersionAfterInsert, reloaded.RowVersion);
    }
}
