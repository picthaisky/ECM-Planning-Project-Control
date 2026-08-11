using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;

namespace CMPlus.Integration.Tests.Persistence;

/// <summary>
/// S9-BE-04 DoD: $R^{cum}$/$D^{cum}$ come from <c>SUM()</c> over <see cref="ProjectFinanceLedger"/>,
/// never a recomputation - proven here against a real <see cref="CmPlusDbContext"/> (EF Core
/// InMemory, per the Docker outage - docs/perf/gantt-frontend-s6.md §3). Also proves tenant scoping
/// (ADR-0002) and the <c>Disbursement</c>-exclusion rule documented on
/// <see cref="IProjectFinanceLedgerReader"/>.
/// </summary>
public class ProjectFinanceLedgerReaderTests
{
    private static readonly DateTimeOffset EffectiveDate = DateTimeOffset.Parse("2026-08-10T00:00:00+07:00");

    [Fact]
    public async Task GetRetentionHeldAsync_Sums_Accrual_And_Release_Rows_Net_Never_Recomputed()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var projectId = Guid.NewGuid();
        var certificateId1 = Guid.NewGuid();
        var certificateId2 = Guid.NewGuid();

        using (var seedContext = factory.CreateContext())
        {
            seedContext.ProjectFinanceLedgers.AddRange(
                ProjectFinanceLedger.CreateRetentionAccrual(tenantId, projectId, certificateId1, 1_080_000.00m, EffectiveDate),
                ProjectFinanceLedger.CreateRetentionAccrual(tenantId, projectId, certificateId2, 500_000.00m, EffectiveDate),
                ProjectFinanceLedger.CreateRetentionRelease(tenantId, projectId, -300_000.00m, EffectiveDate, "Substantial completion release"));
            await seedContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new ProjectFinanceLedgerReader(readContext);

        var retentionHeld = await reader.GetRetentionHeldAsync(projectId);

        Assert.Equal(1_280_000.00m, retentionHeld); // 1,080,000 + 500,000 - 300,000
    }

    [Fact]
    public async Task GetAdvanceRecoveredAsync_Sums_Recovery_And_Adjustment_But_Excludes_Disbursement()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var projectId = Guid.NewGuid();
        var certificateId = Guid.NewGuid();

        using (var seedContext = factory.CreateContext())
        {
            seedContext.ProjectFinanceLedgers.AddRange(
                // Disbursement of the mobilisation advance itself - must NOT count as "recovered".
                new ProjectFinanceLedger(
                    tenantId, projectId, paymentCertificateId: null, FinanceLedgerCategory.Advance,
                    FinanceLedgerEntryType.Disbursement, 48_500_000.00m, EffectiveDate, "Mobilisation advance", null),
                ProjectFinanceLedger.CreateAdvanceRecovery(tenantId, projectId, certificateId, 2_160_000.00m, EffectiveDate),
                ProjectFinanceLedger.CreateAdjustment(
                    tenantId, projectId, null, FinanceLedgerCategory.Advance, -60_000.00m, EffectiveDate, "REF", "correcting an over-recovery"));
            await seedContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new ProjectFinanceLedgerReader(readContext);

        var advanceRecovered = await reader.GetAdvanceRecoveredAsync(projectId);

        // 2,160,000.00 recovery + (-60,000.00) adjustment; the 48,500,000.00 disbursement is excluded.
        Assert.Equal(2_100_000.00m, advanceRecovered);
    }

    [Fact]
    public async Task Readers_Return_Zero_When_No_Ledger_Rows_Exist_Yet()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        using var readContext = factory.CreateContext();
        var reader = new ProjectFinanceLedgerReader(readContext);

        Assert.Equal(0m, await reader.GetRetentionHeldAsync(Guid.NewGuid()));
        Assert.Equal(0m, await reader.GetAdvanceRecoveredAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Readers_Are_Tenant_Scoped_One_Tenants_Ledger_Never_Leaks_Into_Anothers_Sum()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantA);
        var sharedProjectId = Guid.NewGuid(); // same Guid used across tenants on purpose - the
                                               // TenantId filter, not ProjectId, must be what isolates them.

        using (var seedContext = factory.CreateContext())
        {
            seedContext.ProjectFinanceLedgers.Add(
                ProjectFinanceLedger.CreateRetentionAccrual(tenantA, sharedProjectId, Guid.NewGuid(), 1_000_000.00m, EffectiveDate));
            await seedContext.SaveChangesAsync();
        }

        factory.TenantProvider.TenantId = tenantB;
        using (var seedContext = factory.CreateContext())
        {
            seedContext.ProjectFinanceLedgers.Add(
                ProjectFinanceLedger.CreateRetentionAccrual(tenantB, sharedProjectId, Guid.NewGuid(), 9_000_000.00m, EffectiveDate));
            await seedContext.SaveChangesAsync();
        }

        factory.TenantProvider.TenantId = tenantA;
        using var readContext = factory.CreateContext();
        var reader = new ProjectFinanceLedgerReader(readContext);

        var retentionHeld = await reader.GetRetentionHeldAsync(sharedProjectId);

        Assert.Equal(1_000_000.00m, retentionHeld); // Tenant B's 9,000,000.00 must never leak in.
    }

    [Fact]
    public async Task GetRetentionHeldAsync_Reflects_The_Ledger_Directly_Not_A_Recomputation_From_Certificates()
    {
        // S9-BE-04's non-negotiable: even if a PaymentCertificate row exists with its own
        // RetentionAmount, the reader must answer purely from ProjectFinanceLedger rows - proven by
        // seeding a certificate whose RetentionAmount would imply a different number than what is
        // actually posted to the ledger, and asserting the reader reports the ledger's figure.
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var projectId = Guid.NewGuid();

        var certificate = new PaymentCertificate(tenantId, projectId, 1, "IPC 1", 21_600_000.00m, 0m, Guid.NewGuid());
        certificate.SetPeriodClaim(100m, null, null, 21_600_000.00m, 1_080_000.00m, 2_160_000.00m, 18_360_000.00m);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.PaymentCertificates.Add(certificate);
            // Deliberately posts a different amount than certificate.RetentionAmount would suggest,
            // to prove the reader never reaches into PaymentCertificate at all.
            seedContext.ProjectFinanceLedgers.Add(ProjectFinanceLedger.CreateRetentionAccrual(
                tenantId, projectId, certificate.Id, 999_999.99m, EffectiveDate));
            await seedContext.SaveChangesAsync();
        }

        using var readContext = factory.CreateContext();
        var reader = new ProjectFinanceLedgerReader(readContext);

        Assert.Equal(999_999.99m, await reader.GetRetentionHeldAsync(projectId));
    }
}
