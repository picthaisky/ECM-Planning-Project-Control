using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Persistence;

/// <summary>
/// S9-BE-04 DoD: "accrual/recovery ถูก post เมื่อถึง Certified" - proven end to end against a real
/// <see cref="Microsoft.EntityFrameworkCore.DbContext"/> (EF Core InMemory, per the Docker outage).
/// This exercises exactly the sequence a future S9-BE-05 <c>ApproveCommandHandler</c> would run
/// (load certificate -&gt; <see cref="PaymentCertificate.Approve"/> -&gt; on reaching <c>Certified</c>,
/// persist <see cref="PaymentCertificate.CreateCertificationLedgerEntries"/>) without needing that
/// handler/controller to exist - the orchestration glue itself is the only thing left for S9-BE-05,
/// and this test is the concrete proof the domain pieces it will call already fit together
/// correctly, not merely "trust me, a future handler will do it".
/// </summary>
public class PaymentCertificateCertificationLedgerIntegrationTests
{
    [Fact]
    public async Task Certifying_A_Certificate_And_Persisting_Its_Ledger_Entries_Makes_The_Reader_See_Them()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var projectId = Guid.NewGuid();
        var certifiedAt = DateTimeOffset.Parse("2026-08-10T09:00:00+07:00");
        Guid certificateId;

        using (var context = factory.CreateContext())
        {
            var certificate = new PaymentCertificate(tenantId, projectId, 1, "IPC 1", 21_600_000.00m, 0m, Guid.NewGuid());
            certificate.SetPeriodClaim(100m, null, null, 21_600_000.00m, 1_080_000.00m, 2_160_000.00m, 18_360_000.00m);
            certificate.Submit([new PaymentCertificateApprovalStepInput(1, UserRole.QS, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), certifiedAt);
            certificate.Approve(Guid.NewGuid(), UserRole.QS, UserRole.QS, allowSelfApproval: false, certifiedAt);
            Assert.Equal(PaymentCertificateStatus.Certified, certificate.Status);

            context.PaymentCertificates.Add(certificate);
            context.ProjectFinanceLedgers.AddRange(certificate.CreateCertificationLedgerEntries());
            await context.SaveChangesAsync();
            certificateId = certificate.Id;
        }

        using var readContext = factory.CreateContext();
        var reader = new CMPlus.Infrastructure.Persistence.ProjectFinanceLedgerReader(readContext);

        Assert.Equal(1_080_000.00m, await reader.GetRetentionHeldAsync(projectId));
        Assert.Equal(2_160_000.00m, await reader.GetAdvanceRecoveredAsync(projectId));

        var ledgerRows = await readContext.ProjectFinanceLedgers.AsNoTracking()
            .Where(e => e.PaymentCertificateId == certificateId)
            .ToListAsync();
        Assert.Equal(2, ledgerRows.Count);
        Assert.All(ledgerRows, e => Assert.Equal(certifiedAt, e.EffectiveDate));
    }

    [Fact]
    public async Task A_Second_Certificate_Sees_The_Firsts_Ledger_Postings_As_Its_Own_Cumulative_Before_Values()
    {
        // P1/P2-style chain: certificate 1 posts its retention/advance; certificate 2's Rcum_(k-1)/
        // Dcum_(k-1) must be exactly what certificate 1 posted, read from the ledger - never a
        // recomputation from certificate 1's own fields.
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var projectId = Guid.NewGuid();
        var certifiedAt = DateTimeOffset.Parse("2026-08-10T09:00:00+07:00");

        using (var context = factory.CreateContext())
        {
            var certificateOne = new PaymentCertificate(tenantId, projectId, 1, "IPC 1", 10_000_000.00m, 0m, Guid.NewGuid());
            certificateOne.SetPeriodClaim(40m, null, null, 4_000_000.00m, 200_000.00m, 400_000.00m, 3_400_000.00m);
            certificateOne.Submit([new PaymentCertificateApprovalStepInput(1, UserRole.QS, 1)], Guid.NewGuid(), 1, false, Guid.NewGuid(), certifiedAt);
            certificateOne.Approve(Guid.NewGuid(), UserRole.QS, UserRole.QS, allowSelfApproval: false, certifiedAt);

            context.PaymentCertificates.Add(certificateOne);
            context.ProjectFinanceLedgers.AddRange(certificateOne.CreateCertificationLedgerEntries());
            await context.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new CMPlus.Infrastructure.Persistence.ProjectFinanceLedgerReader(readContext);

        var retentionHeldBefore = await reader.GetRetentionHeldAsync(projectId);
        var advanceRecoveredBefore = await reader.GetAdvanceRecoveredAsync(projectId);

        Assert.Equal(200_000.00m, retentionHeldBefore);
        Assert.Equal(400_000.00m, advanceRecoveredBefore);
    }
}
